using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Media;
using Launcher.App.Animations;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class EcosystemView : UserControl
{
    private bool _firstFade = true; // 首次进入页面不淡入，只响应刷新

    public EcosystemView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not EcosystemViewModel vm) return;
            vm.Cards.CollectionChanged += OnCardsChanged;
        };
    }

    /// <summary>搜索/分页刷新（Clear 起手重填）→ 结果列表淡入 + 4px 上移（180ms Standard）</summary>
    private void OnCardsChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Reset && e.NewStartingIndex != 0) return;
        FadeIn(CardsScroll);
    }

    private void FadeIn(Control target)
    {
        if (_firstFade)
        {
            _firstFade = false;
            return;
        }
        if (!target.IsEffectivelyVisible) return;
        target.Opacity = 0;
        var tx = new TranslateTransform(0, 4);
        target.RenderTransform = tx;
        UiAnim.Animate(180, UiAnim.Curves.Standard, e =>
        {
            target.Opacity = e;
            tx.Y = 4 * (1 - e);
        }, null, target);
    }
}
