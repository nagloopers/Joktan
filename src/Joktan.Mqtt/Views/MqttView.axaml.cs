using System;
using Avalonia.Controls;
using Joktan.Mqtt.ViewModels;

namespace Joktan.Mqtt.Views;

public partial class MqttView : UserControl
{
    public MqttView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MqttViewModel vm)
            vm.EditClientSettings = editor => ClientSettingsWindow.ShowOverAsync(this, editor);
    }
}
