namespace Backpack.Compatibility;

internal sealed class RestartRequiredCoordinator
{
    private bool _actionApplied;
    private bool _quitRequested;

    public bool CanQuit => _actionApplied && !_quitRequested;
    public bool QuitRequested => _quitRequested;

    public void MarkActionApplied()
    {
        _actionApplied = true;
    }

    public bool TryQuit(Action quitGame, out string error)
    {
        error = null;
        if (!_actionApplied)
        {
            error = "Quit is unavailable until the legacy Backpack DLL has been quarantined successfully.";
            return false;
        }

        if (_quitRequested)
        {
            error = "A Schedule I quit has already been requested.";
            return false;
        }

        try
        {
            _quitRequested = true;
            quitGame();
            return true;
        }
        catch (Exception exception)
        {
            _quitRequested = false;
            error = $"Schedule I could not be closed cleanly: {exception.Message}";
            return false;
        }
    }
}
