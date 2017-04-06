using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Davenport;
using Davenport.Entities;
using Xunit;

namespace Davenport.Tests
{
    public class ClientTests
    {
        Client<MyTestClass> Client { get; }

        MyTestClass ExampleClass = new MyTestClass()
        {
            Foo = "test value",
            Bar = false,
            Baz = 11,
            Bat = 5,
        };

        public ClientTests()
        {
            var config = new Configuration()
            {
                CouchUrl = "http://localhost:5984",
                DatabaseName = "davenport_net"
            };
            Client = new Client<MyTestClass>(config);

            config.Warning += (object sender, string message) => Console.WriteLine(message);
        }

        [Fact(DisplayName = "Client PostAsync")]
        public async Task PostAsync()
        {
            var doc = await Client.PostAsync(ExampleClass);

            Assert.False(string.IsNullOrEmpty(doc.Id));
            Assert.False(string.IsNullOrEmpty(doc.Rev));
            Assert.True(doc.Ok);
        }

        [Fact(DisplayName = "Client GetAsync")]
        public async Task GetAsync()
        {
            var created = await Client.PostAsync(ExampleClass);
            var doc = await Client.GetAsync(created.Id);

            Assert.Equal(doc.Foo, "test value");
            Assert.False(doc.Bar);
            Assert.Equal(doc.Baz, 11);
            Assert.True(doc.Bat.HasValue);
            Assert.Equal(doc.Bat, 5);
        }

        [Fact(DisplayName = "Client CountAsync")]
        public async Task CountAsync()
        {
            var count = await Client.CountAsync();

            Assert.True(count > 0);
        }

        [Fact(DisplayName = "Client CountWithExpressionAsync")]
        public async Task CountWithExpressionAsync()
        {
            var count = await Client.CountBySelectorAsync(doc => doc.Foo == "test value 2");
            var totalCount = await Client.CountAsync();

            Assert.True(count > 0);
            Assert.True(totalCount > count);
        }

        [Fact(DisplayName = "Client PutAsync")]
        public async Task PutAsync()
        {
            var created = await Client.PostAsync(ExampleClass);
            var retrieved = await Client.GetAsync(created.Id, created.Rev);

            retrieved.Foo = "test value 2";
            retrieved.Bar = true;
            retrieved.Baz = 9;
            retrieved.Bat = null;

            var updated = await Client.PutAsync(created.Id, retrieved, created.Rev);
            retrieved = await Client.GetAsync(updated.Id, updated.Rev);

            Assert.Equal(retrieved.Foo, "test value 2");
            Assert.True(retrieved.Bar);
            Assert.Equal(retrieved.Baz, 9);
            Assert.Null(retrieved.Bat);
            Assert.False(retrieved.Bat.HasValue);
        }

        [Fact(DisplayName = "Client DeleteAsync")]
        public async Task DeleteAsync()
        {
            var created = await Client.PostAsync(ExampleClass);
            
            await Client.DeleteAsync(created.Id, created.Rev);
        }

        [Fact(DisplayName = "Client ListWithDocsAsync")]
        public async Task ListWithDocsAsync()
        {
            var list = await Client.ListWithDocsAsync();

            Assert.NotNull(list);
            Assert.Equal(list.Offset, 0);
            Assert.True(list.Rows.Count() > 0);
            Assert.True(list.DesignDocs.Count() > 0);
            Assert.True(list.Rows.All(row => row.Doc.GetType() == typeof(MyTestClass)));
            Assert.True(list.Rows.All(row => ! row.Id.StartsWith("_design")));
            Assert.True(list.Rows.All(row => ! string.IsNullOrEmpty(row.Doc.Id)));
            Assert.True(list.Rows.All(row => ! string.IsNullOrEmpty(row.Doc.Rev)));
            Assert.True(list.DesignDocs.All(doc => doc.Id.StartsWith("_design")));
            Assert.True(list.DesignDocs.All(doc => doc.Doc != null));;
        }

        [Fact(DisplayName = "Client ListWithoutDocsAsync")]
        public async Task ListWithoutDocsAsync()
        {
            var list = await Client.ListWithoutDocsAsync();

            Assert.NotNull(list);
            Assert.Equal(list.Offset, 0);
            Assert.True(list.Rows.Count() > 0, "Rows.Count should be greater than 0.");
            Assert.True(list.DesignDocs.Count() > 0, "Rows.DesignDocs.Count should be greater than 0.");
            Assert.True(list.Rows.All(row => row.Doc.GetType() == typeof(Revision)), "All row docs should be of type Revision.");
            Assert.True(list.Rows.All(row => ! string.IsNullOrEmpty(row.Doc.Rev)), "All rows should have a Doc.Rev value.");
            Assert.True(list.Rows.All(row => ! row.Id.StartsWith("_design")), "All row ids should not start with _design.");
            Assert.True(list.DesignDocs.All(doc => doc.Id.StartsWith("_design")), "All design doc ids should start with _design.");
            Assert.True(list.DesignDocs.All(doc => doc.Doc != null), "All design doc documents should not be null.");;
        }

        [Fact(DisplayName = "Client FindLambdaAsync")]
        public async Task FindLambdaAsync()
        {
            var equalsResult = await Client.FindAsync(doc => doc.Foo == "test value 2");

            Assert.NotNull(equalsResult);
            Assert.True(equalsResult.All(row => row.Foo == "test value 2"));
            Assert.True(equalsResult.All(row => ! string.IsNullOrEmpty(row.Id)));
            Assert.True(equalsResult.All(row => ! string.IsNullOrEmpty(row.Rev)));

            var notEqualsResult = await Client.FindAsync(doc => doc.Foo != "test value 2");

            Assert.NotNull(notEqualsResult);
            Assert.True(notEqualsResult.All(row => row.Foo != "test value 2"));
            Assert.True(notEqualsResult.All(row => ! string.IsNullOrEmpty(row.Id)));
            Assert.True(notEqualsResult.All(row => ! string.IsNullOrEmpty(row.Rev)));
        }

        [Fact(DisplayName = "Client FindDictionaryAsync")]
        public async Task FindDictionaryAsync()
        {
            var equalsResult = await Client.FindAsync(new Dictionary<string, FindExpression>()
            {
                { "Foo", new FindExpression(ExpressionType.Equal, "test value 2") }
            });

            Assert.NotNull(equalsResult);
            Assert.True(equalsResult.All(row => row.Foo == "test value 2"));
            Assert.True(equalsResult.All(row => ! string.IsNullOrEmpty(row.Id)));
            Assert.True(equalsResult.All(row => ! string.IsNullOrEmpty(row.Rev)));

            var notEqualsResult = await Client.FindAsync(new Dictionary<string, FindExpression>()
            {
                { "Foo", new FindExpression(ExpressionType.NotEqual, "test value 2") }
            });

            Assert.NotNull(notEqualsResult);
            Assert.True(notEqualsResult.All(row => row.Foo != "test value 2"));
            Assert.True(notEqualsResult.All(row => ! string.IsNullOrEmpty(row.Id)));
            Assert.True(notEqualsResult.All(row => ! string.IsNullOrEmpty(row.Rev)));
        }

        [Fact(DisplayName = "Client FindObjectAsync")]
        public async Task FindObjectAsync()
        {
            var equalsResult = await Client.FindAsync(new 
            {
                Foo = new Dictionary<string, object>
                {
                    { "$eq", "test value 2" }
                }
            });

            Assert.NotNull(equalsResult);
            Assert.True(equalsResult.All(row => row.Foo == "test value 2"));
            Assert.True(equalsResult.All(row => ! string.IsNullOrEmpty(row.Id)));
            Assert.True(equalsResult.All(row => ! string.IsNullOrEmpty(row.Rev)));

            var notEqualsResult = await Client.FindAsync(new 
            {
                Foo = new Dictionary<string, object>
                {
                    { "$ne", "test value 2" }
                }
            });

            Assert.NotNull(notEqualsResult);
            Assert.True(notEqualsResult.All(row => row.Foo != "test value 2"));
            Assert.True(notEqualsResult.All(row => ! string.IsNullOrEmpty(row.Id)));
            Assert.True(notEqualsResult.All(row => ! string.IsNullOrEmpty(row.Rev)));
        }

        [Fact(DisplayName = "Client ExistsAsync")]
        public async Task ExistsAsync()
        {
            var created = await Client.PostAsync(ExampleClass);
            var exists = await Client.ExistsAsync(created.Id);
            var existsWithRev = await Client.ExistsAsync(created.Id, created.Rev);

            Assert.True(exists);
            Assert.True(existsWithRev);
        }

        [Fact(DisplayName = "Client ExistsByExpressionAsync")]
        public async Task ExistsByExpressionAsync()
        {
            var exists = await Client.ExistsBySelector(doc => doc.Foo == "test value 2");

            Assert.True(exists);
        }

        [Fact(DisplayName = "Client ExistsByDictionaryAsync")]
        public async Task ExistsByDictionaryAsync()
        {
            var exists = await Client.ExistsBySelector(new Dictionary<string, FindExpression>()
            {
                { "Foo", new FindExpression(ExpressionType.Equal, "test value 2") }
            });

            Assert.True(exists);
        }

        [Fact(DisplayName = "Client ExistsByObjectAsync")]
        public async Task ExistsByObjectAsync()
        {
            var exists = await Client.ExistsBySelector(new 
            {
                Foo = new Dictionary<string, object>
                {
                    { "$eq", "test value 2" }
                }
            });

            Assert.True(exists);
        }

        [Fact(DisplayName = "Client CopyAsync")]
        public async Task CopyAsync()
        {
            var uuid = $"a-unique-string-{DateTime.Now.Millisecond}";
            var createResult = await Client.PostAsync(ExampleClass);
            var copyResult = await Client.CopyAsync(createResult.Id, uuid);

            Assert.Equal(copyResult.Id, uuid);
        }

        [Fact(DisplayName = "Client ViewAsync")]
        public async Task ViewAsync()
        {
            var viewResult = await Client.ViewAsync<int>("list", "only-bazs-greater-than-10");

            Assert.True(viewResult.Count() > 0);
            Assert.True(viewResult.Sum(doc => doc.Value) > 0);
        }
    }
}