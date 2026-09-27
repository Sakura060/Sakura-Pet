using System.Windows;

namespace EriiPet.App;

/// <summary>交互控制器对宿主窗口的最小依赖（把交互逻辑从 MainWindow 解耦）</summary>
public interface IWindowContext
{
    double Left { get; set; }
    double Top { get; set; }
    double Width { get; }
    double Height { get; }
    (double SX, double SY) DpiScale { get; }
    bool CaptureMouse();
    void ReleaseMouseCapture();
    void DispatcherInvoke(Action action);
}
