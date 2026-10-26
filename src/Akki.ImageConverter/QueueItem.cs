using System.ComponentModel;
using System.Runtime.CompilerServices;
using Akki.ImageConverter.Core;

namespace Akki.ImageConverter;

public sealed class QueueItem(ImageSource source) : INotifyPropertyChanged
{
    public ImageSource Source { get; } = source;
    public string Name => System.IO.Path.GetFileName(Source.Path);
    public string Detail => Source.Path;
    private string _status = "Ready";
    public string Status { get => _status; set { _status = value; Changed(); } }
    public string? OutputPath { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
