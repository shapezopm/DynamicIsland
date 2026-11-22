namespace DynamicIsland;

// Separates key-pair ownership from native input so it can be checked deterministically.
internal sealed class SpacePlaybackGesture(bool alreadyDown = false)
{
    internal enum Result { Pass, Consume, Toggle }
    bool _down = alreadyDown;
    bool _claimed;

    internal Result Process(bool down, bool eligible)
    {
        if (down)
        {
            if (_down) return _claimed ? Result.Consume : Result.Pass;
            _down = true;
            _claimed = eligible;
            return _claimed ? Result.Toggle : Result.Pass;
        }
        bool consumed = _claimed;
        _down = _claimed = false;
        return consumed ? Result.Consume : Result.Pass;
    }
}
