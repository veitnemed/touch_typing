using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TouchTypingStudio
{
    internal sealed partial class LiveStudio
    {
        // Scoped to the Live catalog and its configuration form. This is a real
        // scrollbar (thumb, page actions, scroll automation, wheel and BringIntoView).
        private static void ApplyLiveScrollStyle(ScrollViewer viewer)
        {
            var style = new Style(typeof(ScrollBar));
            style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 12.0));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Ui.Brush("#201932")));
            var template = new ControlTemplate(typeof(ScrollBar));
            var root = new FrameworkElementFactory(typeof(Border));
            root.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            root.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            root.SetValue(Border.PaddingProperty, new Thickness(2, 1, 2, 1));
            var thumbTemplate = new ControlTemplate(typeof(Thumb));
            var capsule = new FrameworkElementFactory(typeof(Border), "LiveScrollCapsule");
            capsule.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            capsule.SetValue(Border.BackgroundProperty, Ui.Brush("#70559E"));
            thumbTemplate.VisualTree = capsule;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Ui.Brush("#AB86E9"), "LiveScrollCapsule"));
            thumbTemplate.Triggers.Add(hover);
            var drag = new Trigger { Property = Thumb.IsDraggingProperty, Value = true };
            drag.Setters.Add(new Setter(Border.BackgroundProperty, Ui.Brush("#C5A5FA"), "LiveScrollCapsule"));
            thumbTemplate.Triggers.Add(drag);
            // Concrete controls keep the native ScrollBar track contract.
            var realTrack = new FrameworkElementFactory(typeof(LiveVerticalTrack), "PART_Track");
            realTrack.SetValue(Track.IsDirectionReversedProperty, true);
            realTrack.SetValue(Track.OrientationProperty, Orientation.Vertical);
            realTrack.SetValue(LiveVerticalTrack.ThumbLookProperty, thumbTemplate);
            root.AppendChild(realTrack);
            template.VisualTree = root;
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            viewer.Resources[typeof(ScrollBar)] = style;
        }

        private sealed class LiveVerticalTrack : Track
        {
            public static readonly DependencyProperty ThumbLookProperty =
                DependencyProperty.Register("ThumbLook", typeof(ControlTemplate), typeof(LiveVerticalTrack),
                    new PropertyMetadata(null, (target, change) => ((LiveVerticalTrack)target).Thumb.Template = (ControlTemplate)change.NewValue));

            public LiveVerticalTrack()
            {
                Thumb = new Thumb { MinHeight = 25, Focusable = false, IsTabStop = false };
                DecreaseRepeatButton = MakePageButton(ScrollBar.PageUpCommand);
                IncreaseRepeatButton = MakePageButton(ScrollBar.PageDownCommand);
            }

            private static RepeatButton MakePageButton(System.Windows.Input.RoutedCommand command)
            {
                var button = new RepeatButton { Command = command, Background = Brushes.Transparent,
                    Opacity = 0, Focusable = false, IsTabStop = false };
                return button;
            }
        }
    }
}
