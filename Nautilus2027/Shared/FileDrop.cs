#nullable disable
using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;

namespace Nautilus2027
{
    /// <summary>
    /// Lets an element (typically a tool's file list / log TextBox) accept dropped files and hand them to
    /// the same handler as the tool's drag area:
    ///
    ///     &lt;TextBox local:FileDrop.Handler="LuacDragDropHandler" ... /&gt;
    ///
    /// The handler is a method <c>void Name(object sender, DragEventArgs e)</c> on the tab (or any parent).
    ///
    /// A plain TextBox can't be used with Drop="..." for files: it takes over drag and drop for text and
    /// refuses files, so the Drop event never fires. This hooks the Preview events instead, so files are
    /// accepted while dragging text inside the box still works normally.
    /// </summary>
    public static class FileDrop
    {
        public static readonly DependencyProperty HandlerProperty = DependencyProperty.RegisterAttached(
            "Handler", typeof(string), typeof(FileDrop), new PropertyMetadata(null, OnHandlerChanged));

        public static string GetHandler(DependencyObject element) => (string)element.GetValue(HandlerProperty);

        public static void SetHandler(DependencyObject element, string value) => element.SetValue(HandlerProperty, value);

        private static void OnHandlerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is UIElement element))
                return;

            element.PreviewDragEnter -= OnPreviewDragOver;
            element.PreviewDragOver -= OnPreviewDragOver;
            element.PreviewDrop -= OnPreviewDrop;

            if (e.NewValue is string name && name.Length > 0)
            {
                element.AllowDrop = true;
                element.PreviewDragEnter += OnPreviewDragOver;
                element.PreviewDragOver += OnPreviewDragOver;
                element.PreviewDrop += OnPreviewDrop;
            }
        }

        private static void OnPreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private static void OnPreviewDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return; // not files: let the TextBox handle it as normal

            // Handled here so the drop isn't processed again by parents with their own Drop handler.
            e.Handled = true;

            var source = (DependencyObject)sender;
            string name = GetHandler(source);
            var signature = new[] { typeof(object), typeof(DragEventArgs) };
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            for (DependencyObject current = source; current != null; current = GetParent(current))
            {
                MethodInfo method = current.GetType().GetMethod(name, flags, null, signature, null);
                if (method == null)
                    continue;
                try
                {
                    method.Invoke(current, new object[] { sender, e });
                }
                catch (TargetInvocationException ex) when (ex.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                }
                return;
            }

            Debug.Fail($"FileDrop: no method '{name}(object, DragEventArgs)' found on {sender.GetType().Name} or its parents.");
        }

        private static DependencyObject GetParent(DependencyObject current)
        {
            var parent = LogicalTreeHelper.GetParent(current);
            if (parent == null && (current is Visual || current is System.Windows.Media.Media3D.Visual3D))
                parent = VisualTreeHelper.GetParent(current);
            return parent;
        }
    }
}
