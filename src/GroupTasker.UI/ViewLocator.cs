using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using GroupTasker.UI.ViewModels;

namespace GroupTasker.UI;

/// <summary>
/// Map a view-model instance to its view. Uses a static dictionary instead of the
/// reflection-based template the Avalonia template generates, so trimming/AOT can't
/// silently strip the lookup.
/// </summary>
public class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Func<Control>> Map = new()
    {
        // Add new VM → View mappings here as the app grows.
    };

    public Control? Build(object? param)
    {
        if (param is null) return null;
        if (Map.TryGetValue(param.GetType(), out var factory))
            return factory();
        return new TextBlock { Text = "Not Found: " + param.GetType().FullName };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
