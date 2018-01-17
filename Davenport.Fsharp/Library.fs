module Davenport.Fsharp.Wrapper

open Davenport.Fsharp.Infrastructure
open Davenport.Entities
open Newtonsoft.Json
open System
open System.Linq.Expressions
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Linq.RuntimeHelpers

type Find =
    | EqualTo of obj
    | NotEqualTo of obj
    | GreaterThan of obj
    | LesserThan of obj
    | GreaterThanOrEqualTo of obj
    | LessThanOrEqualTo of obj
    with
    member x.ToFindExpression() =
        match x with
        | EqualTo x -> ExpressionType.Equal, x
        | NotEqualTo x -> ExpressionType.NotEqual, x
        | GreaterThan x -> ExpressionType.GreaterThan, x
        | LesserThan x -> ExpressionType.LessThan, x
        | GreaterThanOrEqualTo x -> ExpressionType.GreaterThanOrEqual, x
        | LessThanOrEqualTo x -> ExpressionType.LessThanOrEqual, x
        |> FindExpression

type CouchProps = private {
    username: string option
    password: string option
    converter: JsonConverter option
    databaseName: string
    couchUrl: string
    id: string
    rev: string
}

let private defaultCouchProps() = {
        username = None
        password = None
        converter = None
        databaseName = ""
        couchUrl = ""
        id = "_id"
        rev = "_rev"
    }

let private toConfig<'doctype> (props: CouchProps) =
    let config = Davenport.Configuration(props.couchUrl, props.databaseName)
    config.Username <- Option.defaultValue "" props.username
    config.Password <- Option.defaultValue "" props.password
    config.Converter <- FsConverter<'doctype>(props.id, props.rev, props.converter) :> JsonConverter
    config

let private toClient<'doctype> = toConfig<'doctype> >> Davenport.Client<FsDoc<'doctype>>

/// Converts an F# expression to a LINQ expression, then converts that LINQ expression to a Map<string, Find> due to an incompatibility with the FsDoc and the types expected by Find, Exists and CountByExpression functions.
let private convertExprToMap<'a> (expr : Expr<'a -> bool>) =
    /// Source: https://stackoverflow.com/a/23390583
    let linq = LeafExpressionConverter.QuotationToExpression expr
    let call = linq :?> MethodCallExpression
    let lambda = call.Arguments.[0] :?> LambdaExpression

    Expression.Lambda<Func<'a, bool>>(lambda.Body, lambda.Parameters)
    |> Davenport.Infrastructure.ExpressionParser.Parse

let private listedRowToDoctypeRow<'doctype> (row: ListedRow<FsDoc<'doctype>>) =
    let newRow = ListedRow<'doctype>()
    newRow.Doc <- Option.get row.Doc.Data
    newRow.Id <- row.Id
    newRow.Key <- row.Key
    newRow.Value <- row.Value

    newRow

let private convertMapToDict (map: Map<string, Find>) =
    Map.map (fun _ (value: Find) -> value.ToFindExpression()) map
    |> Collections.Generic.Dictionary

let private convertPostPutCopyResponse (r: PostPutCopyResponse) =
    // Convert 'Ok' prop to false if it's null.
    { Id = r.Id
      Rev = r.Rev
      Ok = Option.ofNullable r.Ok |> Option.defaultValue false }

let private asyncMap (fn: 't -> 'u) task = async {
    let! result = task

    return fn result
}

let private asyncMapSeq (fn: 't -> 'u) task = async {
    let! result = task

    return Seq.map fn result
}

let database name couchUrl =
    { defaultCouchProps() with databaseName = name; couchUrl = couchUrl }

let username username config = { config with username = Some username }

let password password config = { config with password = Some password }

let idName name props = { props with id = name }

let revName name props = { props with rev = name }

let converter converter props = { props with converter = Some converter }

let getCouchVersion props =
    toConfig props
    |> Davenport.Configuration.GetVersionAsync
    |> Async.AwaitTask

let isVersion2OrAbove = Davenport.Configuration.IsVersion2OrAbove

/// Creates a CouchDB database if it doesn't exist.
let createDatabase props =
    toConfig props
    |> Davenport.Configuration.CreateDatabaseAsync
    |> Async.AwaitTask

/// Creates the given design docs. Will check that each view in each design doc has functions that perfectly match the ones found in the database, and update them if they don't match.
/// Will throw an ArgumentException if no design docs are given.
let createDesignDocs (docs: Davenport.Entities.DesignDocConfig seq) props =
    let config = toConfig props

    Davenport.Configuration.CreateDesignDocsAsync(config, docs)
    |> Async.AwaitTask

/// Creates indexes for the given fields. This makes querying with the Find methods and selectors faster.
/// Will throw an ArgumentException if no indexes are given.
let createIndexes (indexes: string seq) props =
    let config = toConfig props

    Davenport.Configuration.CreateDatabaseIndexesAsync(config, indexes)
    |> Async.AwaitTask

/// Combines the createDatabase, createDesignDocs and createIndexes functions, running all three at once.
let configureDatabase (designDocs: Davenport.Entities.DesignDocConfig seq) (indexes: string seq) props = async {
    let config = toConfig props

    do!
        Davenport.Configuration.ConfigureDatabaseAsync<FsDoc<obj>>(config, indexes, designDocs)
        |> Async.AwaitTask
        |> Async.Ignore
}

/// Deletes the database. This cannot be undone!
let deleteDatabase props =
    let client = toClient props

    client.DeleteDatabaseAsync()
    |> Async.AwaitTask

/// Creates the given document and assigns a random id. If you want to choose the id, use the createWithId.
let create<'doctype> data props =
    let client = toClient<'doctype> props
    let doc = FsDoc()
    doc.Data <- Some data

    client.PostAsync doc
    |> Async.AwaitTask
    |> asyncMap convertPostPutCopyResponse

/// Creates the document using the given id. This will result in a 409 conflict error if the id is already taken.
let createWithId<'doctype> id data props =
    let client = toClient<'doctype> props
    let doc = FsDoc()
    doc.Data <- Some data

    // Passing a null rev to the PutAsync function will create the doc using the specified id.
    client.PutAsync(id, doc, null)
    |> Async.AwaitTask
    |> asyncMap convertPostPutCopyResponse

/// Updates the document with the given id and revision.
let update<'doctype> id rev data props =
    let client = toClient<'doctype> props
    let doc = FsDoc()
    doc.Data <- Some data

    client.PutAsync(id, doc, rev)
    |> Async.AwaitTask
    |> asyncMap convertPostPutCopyResponse

/// Copies the document with the oldId and assigns the newId to the copy.
let copy oldId newId props =
    let client = toClient props

    client.CopyAsync(oldId, newId)
    |> Async.AwaitTask
    |> asyncMap convertPostPutCopyResponse

/// Deletes the document with the given id and revision.
let delete id rev props =
    let client = toClient props

    client.DeleteAsync(id, rev)
    |> Async.AwaitTask

/// Executes the view with the given designDocName and viewName.
let view<'returnType> designDocName viewName (viewOptions: ViewOptions option) props =
    let client = toClient props
    let options = Option.toObj viewOptions

    client.ViewAsync(designDocName, viewName, options)
    |> Async.AwaitTask

/// Gets the document with the given id. If a revision is given, that specific version will be returned.
let get<'doctype> id (rev: string option) props = async {
    let client = toClient<'doctype> props
    let! doc =
        client.GetAsync(id, Option.toObj rev)
        |> Async.AwaitTask
        |> Async.Catch

    return
        match doc with
        | Choice1Of2 doc -> Option.get doc.Data |> Some // We want this to fail if, for some reason, the jsonconverter was unable to convert FsDoc.Data
        | Choice2Of2 exn ->
            match exn with
            | :? Davenport.Infrastructure.DavenportException as exn when exn.StatusCode = 404 -> None
            | _ -> raise exn
}

/// Lists all documents on the database.
let listWithDocs<'doctype> (listOptions: ListOptions option) props = async {
    let client = toClient<'doctype> props
    let options = Option.toObj listOptions

    let! result =
        client.ListWithDocsAsync options
        |> Async.AwaitTask

    let newRows =
        result.Rows
        |> Seq.map listedRowToDoctypeRow<'doctype>

    let response = ListResponse<'doctype>()
    response.DesignDocs <- result.DesignDocs
    response.Offset <- result.Offset
    response.Rows <- newRows
    response.TotalRows <- result.TotalRows

    return response
}

/// Lists all documents on the database, but does not return the documents themselves.
let listWithoutDocs (listOptions: ListOptions option) props =
    let client = toClient props
    let options = Option.toObj listOptions

    client.ListWithoutDocsAsync options
    |> Async.AwaitTask

let private findByDictionary<'doctype> selector (findOptions: FindOptions option) props =
    let client = toClient<'doctype> props
    let options = Option.toObj findOptions

    client.FindBySelectorAsync (selector, options)
    |> Async.AwaitTask
    |> asyncMapSeq (fun doc -> Option.get doc.Data)

/// Searches for documents matching the given selector.
let findBySelector<'doctype> = convertMapToDict >> findByDictionary<'doctype>

/// Searches for documents matching the given selector.
/// Usage: findByExpr<DocType> (<@ fun (c: DocType) -> c.SomeProp = SomeValue @>)
/// NOTE: Davenport currently only supports simple 1 argument selectors.
let findByExpr<'doctype> = convertExprToMap<'doctype> >> findByDictionary<'doctype>

/// Returns a count of all documents, *including design documents*.
let count props =
    let client = toClient props

    client.CountAsync()
    |> Async.AwaitTask

let private countByDictionary selector props =
    let client = toClient props

    client.CountBySelectorAsync selector
    |> Async.AwaitTask

/// Retrieves a count of all documents matching the given selector.
let countBySelector = convertMapToDict >> countByDictionary

/// Retrieves a count of all documents matching the given selector.
/// Usage: countByExpr<DocType> (<@ fun (c: DocType) -> c.SomeProp = SomeValue @>)
/// NOTE: Davenport currently only supports simple 1 argument selectors.
let countByExpr<'doctype> = convertExprToMap<'doctype> >> countByDictionary

/// Checks whether a document with the given id exists. If a revision is given, it will check whether that specific version exists.
let exists id (rev: string option) props =
    let client = toClient props

    client.ExistsAsync(id, Option.toObj rev)
    |> Async.AwaitTask

let private existsByDictionary selector props =
    let client = toClient props

    client.ExistsBySelectorAsync selector
    |> Async.AwaitTask

/// Checks that a document matching the given selector exists.
let existsBySelector = convertMapToDict >> existsByDictionary

/// Checks that a document matching the given selector exists.
/// Usage: existsByExpr<DocType> (<@ fun (c: DocType) -> c.SomeProp = SomeValue @>)
/// NOTE: Davenport currently only supports simple 1 argument selectors.
let existsByExpr<'doctype> = convertExprToMap<'doctype> >> existsByDictionary