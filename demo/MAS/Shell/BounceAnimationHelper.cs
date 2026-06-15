using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MAS.Shell;

internal static class BounceAnimationHelper
{
    internal static Storyboard CreateBounce(Rectangle bar, double containerWidth)
    {
        var travel = containerWidth - bar.Width;
        if (travel <= 0) travel = 100;

        var forward = new DoubleAnimation(0, travel, TimeSpan.FromMilliseconds(1200))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        var backward = new DoubleAnimation(travel, 0, TimeSpan.FromMilliseconds(1200))
        {
            BeginTime = TimeSpan.FromMilliseconds(1200),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        var storyboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        Storyboard.SetTarget(forward, bar);
        Storyboard.SetTargetProperty(forward, new PropertyPath(Canvas.LeftProperty));
        Storyboard.SetTarget(backward, bar);
        Storyboard.SetTargetProperty(backward, new PropertyPath(Canvas.LeftProperty));
        storyboard.Children.Add(forward);
        storyboard.Children.Add(backward);
        return storyboard;
    }
}
