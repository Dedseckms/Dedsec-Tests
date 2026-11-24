using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Dedsec.Tests;
internal static class DialogChecks
{
    internal static void Run()
    {
        if(!Environment.GetCommandLineArgs().Contains("--capture"))throw new InvalidOperationException("Isolated settings required.");Exception? failure=null;var thread=new Thread(()=>
        {
            try
            {
                Console.WriteLine("DIALOG load resources");var app=new Dedsec.Tests.App();app.InitializeComponent();EventManager.RegisterClassHandler(typeof(MainWindow),FrameworkElement.LoadedEvent,new RoutedEventHandler((_,e)=>e.Handled=true));Console.WriteLine("DIALOG create main");var owner=new MainWindow{WindowStartupLocation=WindowStartupLocation.Manual,Left=-16000,Top=-16000,ShowActivated=false,ShowInTaskbar=false};app.MainWindow=owner;app.ShutdownMode=ShutdownMode.OnMainWindowClose;owner.Show();Console.WriteLine("DIALOG main shown");
                var settings=(Settings)typeof(MainWindow).GetField("settings",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;
                foreach(bool tray in new[]{false,true})
                {
                    settings.Tray=tray;var dialog=new BrandedDialog(owner,"Изменение размера раздела","Применить"){WindowStartupLocation=WindowStartupLocation.Manual,Left=-16000,Top=-16000,ShowActivated=false};dialog.Body.Children.Add(new ResizeEditor(100*StorageBackend.MiB,300*StorageBackend.MiB,200*StorageBackend.MiB));dialog.Show();if(tray)owner.WindowState=WindowState.Minimized;
                    Console.WriteLine("DIALOG editor shown tray="+tray);var close=Buttons(dialog).First(button=>button.Content?.ToString()=="✕");close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Console.WriteLine("DIALOG editor closed");var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Console.WriteLine("DIALOG restore dispatched");
                    if(!owner.IsVisible||owner.WindowState==WindowState.Minimized||!app.Windows.Cast<Window>().Contains(owner)||dialog.IsVisible)throw new Exception("Closing the owned editor hid or closed the main application.");Console.WriteLine("PASS Editor X closes only the editor and restores main window; tray="+tray);
                }
                settings.Tray=false;owner.Close();app.Shutdown();
            }catch(Exception error){failure=error;}
        });thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();if(!thread.Join(TimeSpan.FromSeconds(20)))throw new TimeoutException("Dialog close regression timed out");if(failure!=null)throw failure;
    }
    private static IEnumerable<Button> Buttons(DependencyObject root){if(root is Button button)yield return button;for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Buttons(System.Windows.Media.VisualTreeHelper.GetChild(root,i)))yield return child;}
}
