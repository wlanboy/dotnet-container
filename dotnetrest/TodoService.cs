// TimeProvider statt DateTime.Now, damit Tests die Uhrzeit festlegen können (FakeTimeProvider)
public class TodoService(TimeProvider timeProvider)
{
    private readonly Todo[] _sampleTodos = CreateSampleTodos(DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime));

    private static Todo[] CreateSampleTodos(DateOnly today) =>
    [
        new(1, "Walk the dog"),
        new(2, "Do the dishes", today),
        new(3, "Do the laundry", today.AddDays(1)),
        new(4, "Clean the bathroom"),
        new(5, "Clean the car", today.AddDays(2))
    ];

    public Todo[] GetAll() => _sampleTodos;

    public Todo? GetById(int id) => _sampleTodos.FirstOrDefault(a => a.Id == id);
}
