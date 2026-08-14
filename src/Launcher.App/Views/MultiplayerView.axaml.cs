using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Launcher.App.Animations;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

/// <summary>
/// 联机页：区块（欢迎 / 忙碌 / 就绪）切换时 UiAnim.PopIn 弹入，
/// 欢迎态 tab 切换时对内容区同样弹入——项目动画风格。
/// </summary>
public partial class MultiplayerView : UserControl
{
    private MultiplayerViewModel? _vm;

    public MultiplayerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) =>
        {
            if (DataContext is MultiplayerViewModel vm) _ = vm.OnPageLoadedAsync();
        };
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as MultiplayerViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var vm = _vm;
        if (vm is null) return;

        // 区块切换：等 IsVisible 生效后再弹入
        Dispatcher.UIThread.Post(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(MultiplayerViewModel.Step):
                {
                    Visual? block = vm.Step switch
                    {
                        MultiplayerPageStep.Welcome => WelcomeBlock,
                        MultiplayerPageStep.Busy => BusyBlock,
                        MultiplayerPageStep.Active => ActiveBlock,
                        MultiplayerPageStep.Declined => DeclinedBanner,
                        _ => null,
                    };
                    if (block is not null) UiAnim.PopIn(block);
                    break;
                }
                case nameof(MultiplayerViewModel.IsCreateTab):
                    UiAnim.PopIn(CreateTabContent);
                    break;
                case nameof(MultiplayerViewModel.IsJoinTab):
                    UiAnim.PopIn(JoinTabContent);
                    break;
            }
        });
    }
}
