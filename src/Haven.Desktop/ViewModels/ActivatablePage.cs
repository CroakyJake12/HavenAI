namespace Haven.Desktop.ViewModels;

public interface IActivatablePage
{
    Task ActivateAsync(CancellationToken cancellationToken);
    void Deactivate();
}

