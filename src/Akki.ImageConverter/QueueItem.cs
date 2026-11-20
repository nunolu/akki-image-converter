using System.ComponentModel;
using System.Runtime.CompilerServices;
using Akki.ImageConverter.Core;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Akki.ImageConverter;

public sealed class QueueItem(ImageSource source) : INotifyPropertyChanged
{
    public ImageSource Source { get; } = source;
    public string Name => System.IO.Path.GetFileName(Source.Path);
    public string Detail => Source.Path;
    public BitmapImage Thumbnail => new(new Uri(System.IO.Path.GetFullPath(Source.Path)));
    private string _status = "Ready";
    public string Status { get => _status; set { _status = value; Changed(); } }
    public string? OutputPath { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
