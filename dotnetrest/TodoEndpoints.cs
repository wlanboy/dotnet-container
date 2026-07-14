using Microsoft.AspNetCore.Http.HttpResults;

internal static class TodoEndpoints
{
    public static void MapTodoEndpoints(this WebApplication app)
    {
        var todosApi = app.MapGroup("/todos");

        todosApi.MapGet("/", (TodoService todoService) => todoService.GetAll())
            .WithName("GetTodos");

        todosApi.MapGet("/{id}", Results<Ok<Todo>, NotFound> (int id, TodoService todoService) =>
            todoService.GetById(id) is { } todo
                ? TypedResults.Ok(todo)
                : TypedResults.NotFound())
            .WithName("GetTodoById");
    }
}
