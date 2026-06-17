using TodoListMcp.Core;
using TodoListMcp.Core.Model;

namespace TodoListMcp.Core.Tests;

public class SearchTests
{
    [Fact]
    public void Text_search_matches_title_and_comments_case_insensitively()
    {
        var doc = TestData.Sample();

        Assert.Equal(new[] { 1 }, Ids(doc.Search(new() { Text = "parent" })));      // title + comments
        Assert.Equal(new[] { 1 }, Ids(doc.Search(new() { Text = "NOTES" })));        // comments only
        Assert.Equal(new[] { 2 }, Ids(doc.Search(new() { Text = "child a" })));
    }

    [Fact]
    public void Filter_by_category()
    {
        var doc = TestData.Sample();
        Assert.Equal(new[] { 1 }, Ids(doc.Search(new() { Category = "Work" })));
        Assert.Empty(doc.Search(new() { Category = "Nope" }));
    }

    [Fact]
    public void Filter_by_person()
    {
        var doc = TestData.Sample();
        Assert.Equal(new[] { 2 }, Ids(doc.Search(new() { Person = "Jane" })));
        Assert.Equal(new[] { 3 }, Ids(doc.Search(new() { Person = "Mary" })));
    }

    [Fact]
    public void Filter_by_completion()
    {
        var doc = TestData.Sample();
        Assert.Equal(new[] { 2 }, Ids(doc.Search(new() { Completed = true })));
        Assert.Equal(new[] { 1, 3 }, Ids(doc.Search(new() { Completed = false })));
    }

    [Fact]
    public void Filter_by_minimum_priority()
    {
        var doc = TestData.Sample();
        // Priorities: parent=5, childA=8, childB=2.
        Assert.Equal(new[] { 1, 2 }, Ids(doc.Search(new() { MinPriority = 5 })));
        Assert.Equal(new[] { 2 }, Ids(doc.Search(new() { MinPriority = 8 })));
    }

    [Fact]
    public void Criteria_combine_with_and()
    {
        var doc = TestData.Sample();
        Assert.Empty(doc.Search(new() { Completed = true, Person = "Mary" }));
        Assert.Equal(new[] { 2 }, Ids(doc.Search(new() { Completed = true, Person = "Bob" })));
    }

    private static int[] Ids(IReadOnlyList<TodoTask> tasks) => tasks.Select(t => t.Id).OrderBy(x => x).ToArray();
}
