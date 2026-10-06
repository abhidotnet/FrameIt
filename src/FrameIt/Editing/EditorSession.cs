using System.Drawing;

namespace FrameIt.Editing;

public sealed class EditorSession : IDisposable
{
    private const int MaxHistory = 40;
    private readonly List<SwapCommand> _commands = new();
    private Bitmap _image;
    private int _index;
    private int _cleanIndex;
    private bool _disposed;

    public EditorSession(Bitmap image, bool startDirty)
    {
        _image = ImageEffects.CloneArgb(image);
        Annotations = new List<Annotation>();
        Redactions = new List<Redaction>();
        _cleanIndex = startDirty ? -1 : 0;
    }

    public Bitmap Image => _image;

    public List<Annotation> Annotations { get; private set; }

    public List<Redaction> Redactions { get; private set; }

    public bool CanUndo => _index > 0;

    public bool CanRedo => _index < _commands.Count;

    public bool IsDirty => _index != _cleanIndex;

    public event Action? Changed;

    public void Apply(Bitmap? newImage, List<Annotation>? newAnnotations, List<Redaction>? newRedactions)
    {
        Push(new SwapCommand(this, newImage, newAnnotations, newRedactions), alreadyApplied: false);
    }

    public void CommitApplied(List<Annotation>? previousAnnotations, List<Redaction>? previousRedactions)
    {
        Push(new SwapCommand(this, null, previousAnnotations, previousRedactions), alreadyApplied: true);
    }

    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        _index--;
        _commands[_index].Undo();
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        _commands[_index].Redo();
        _index++;
        Changed?.Invoke();
    }

    public void MarkClean()
    {
        _cleanIndex = _index;
    }

    public void Restore(List<Annotation>? annotations, List<Redaction>? redactions)
    {
        if (annotations is not null)
        {
            Annotations = annotations;
        }

        if (redactions is not null)
        {
            Redactions = redactions;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var command in _commands)
        {
            command.Dispose();
        }

        _commands.Clear();
        _image.Dispose();
    }

    internal Bitmap ExchangeImage(Bitmap replacement)
    {
        var previous = _image;
        _image = replacement;
        return previous;
    }

    internal List<Annotation> ExchangeAnnotations(List<Annotation> replacement)
    {
        var previous = Annotations;
        Annotations = replacement;
        return previous;
    }

    internal List<Redaction> ExchangeRedactions(List<Redaction> replacement)
    {
        var previous = Redactions;
        Redactions = replacement;
        return previous;
    }

    private void Push(SwapCommand command, bool alreadyApplied)
    {
        while (_commands.Count > _index)
        {
            var dropped = _commands[^1];
            _commands.RemoveAt(_commands.Count - 1);
            dropped.Dispose();
        }

        if (_cleanIndex > _index)
        {
            _cleanIndex = -1;
        }

        if (!alreadyApplied)
        {
            command.Redo();
        }

        _commands.Add(command);
        _index++;
        TrimHistory();
        Changed?.Invoke();
    }

    private void TrimHistory()
    {
        var overflow = _commands.Count - MaxHistory;
        if (overflow <= 0)
        {
            return;
        }

        for (var index = 0; index < overflow; index++)
        {
            _commands[index].Dispose();
        }

        _commands.RemoveRange(0, overflow);
        _index -= overflow;
        _cleanIndex -= overflow;
    }

    private sealed class SwapCommand : IDisposable
    {
        private readonly EditorSession _session;
        private Bitmap? _image;
        private List<Annotation>? _annotations;
        private List<Redaction>? _redactions;

        public SwapCommand(
            EditorSession session,
            Bitmap? image,
            List<Annotation>? annotations,
            List<Redaction>? redactions)
        {
            _session = session;
            _image = image;
            _annotations = annotations;
            _redactions = redactions;
        }

        public void Redo() => Swap();

        public void Undo() => Swap();

        public void Dispose()
        {
            _image?.Dispose();
            _image = null;
        }

        private void Swap()
        {
            if (_image is not null)
            {
                _image = _session.ExchangeImage(_image);
            }

            if (_annotations is not null)
            {
                _annotations = _session.ExchangeAnnotations(_annotations);
            }

            if (_redactions is not null)
            {
                _redactions = _session.ExchangeRedactions(_redactions);
            }
        }
    }
}
