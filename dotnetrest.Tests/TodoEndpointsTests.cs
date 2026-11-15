using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

public class TodoEndpointsTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetTodos_ReturnsAllSampleTodos()
    {
        var todos = await _client.GetFromJsonAsync<Todo[]>("/todos", TestContext.Current.CancellationToken);

        Assert.NotNull(todos);
        Assert.Equal([1, 2, 3, 4, 5], todos.Select(t => t.Id));
    }

    [Fact]
    public async Task GetTodos_DueDatesFollowTimeProvider()
    {
        var todos = await _client.GetFromJsonAsync<Todo[]>("/todos", TestContext.Current.CancellationToken);

        Assert.NotNull(todos);
        Assert.Equal(new DateOnly(2026, 1, 15), todos.Single(t => t.Id == 2).DueBy);
        Assert.Equal(new DateOnly(2026, 1, 16), todos.Single(t => t.Id == 3).DueBy);
        Assert.Equal(new DateOnly(2026, 1, 17), todos.Single(t => t.Id == 5).DueBy);
    }

    [Fact]
    public async Task GetTodoById_ExistingId_Returns200()
    {
        var response = await _client.GetAsync("/todos/2", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var todo = await response.Content.ReadFromJsonAsync<Todo>(TestContext.Current.CancellationToken);
        Assert.Equal(new Todo(2, "Do the dishes", new DateOnly(2026, 1, 15)), todo);
    }

    [Fact]
    public async Task GetTodoById_UnknownId_Returns404()
    {
        var response = await _client.GetAsync("/todos/999", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTodoById_JsonFormat_IsCamelCaseWithIsoDate()
    {
        var response = await _client.GetAsync("/todos/3", TestContext.Current.CancellationToken);

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(3, root.GetProperty("id").GetInt32());
        Assert.Equal("Do the laundry", root.GetProperty("title").GetString());
        Assert.Equal("2026-01-16", root.GetProperty("dueBy").GetString());
        Assert.False(root.GetProperty("isComplete").GetBoolean());
    }

    [Fact]
    public async Task GetTodoById_WithoutDueDate_SerializesNull()
    {
        var json = await _client.GetStringAsync("/todos/1", TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("dueBy").ValueKind);
    }
}
