using System.Windows.Media;
namespace Wpf.CoverShow.FlowComponent
{
    public interface IThumbnailManager
    {
        ImageSource GetThumbnail(string host, string path);
    }
}
