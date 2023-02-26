// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using Amethyst.Utils;
using System.Threading.Tasks;
using Amethyst.Classes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Amethyst.Pages;

/// <summary>
///     An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class Plugins : Page, INotifyPropertyChanged
{
    private bool _pluginsPageLoadedOnce;

    public Plugins()
    {
        InitializeComponent();

        Logger.Info($"Constructing page: '{GetType().FullName}'...");
        // TODO TeachingTips

        Logger.Info("Registering a detached binary semaphore " +
                    $"reload handler for '{GetType().FullName}'...");

        Task.Run(() =>
        {
            Shared.Events.ReloadPluginsPageEvent =
                new ManualResetEvent(false);

            while (true)
            {
                // Wait for a reload signal (blocking)
                Shared.Events.ReloadPluginsPageEvent.WaitOne();

                // Reload & restart the waiting loop
                if (_pluginsPageLoadedOnce && Interfacing.CurrentPageTag == "plugins")
                    Shared.Main.DispatcherQueue.TryEnqueue(Page_LoadedHandler);

                // Reset the event
                Shared.Events.ReloadPluginsPageEvent.Reset();
            }
        });
    }

    // MVVM stuff
    public event PropertyChangedEventHandler PropertyChanged;

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        Logger.Info($"Re/Loading page: '{GetType().FullName}'...");
        Interfacing.CurrentAppState = "plugins";

        // Execute the handler
        Page_LoadedHandler();

        // Mark as loaded
        _pluginsPageLoadedOnce = true;
    }

    private void Page_LoadedHandler()
    {
        OnPropertyChanged(); // Just everything
    }

    private void OnPropertyChanged(string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
    }

    private void SearchTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
    }

    private void SearcherGrid_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
    }

    private void SearcherGrid_DropCompleted(UIElement sender, DropCompletedEventArgs args)
    {
    }

    private void Grid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (MainSplitView is null) return; // Give up before
        MainSplitView.OpenPaneLength = e.NewSize.Width - 700;

        // Optionally close the whole thing if it doesn't fit
        MainSplitView.IsPaneOpen = MainSplitView.OpenPaneLength >= 300;

        SecondarySectionNameTextBlock.Visibility = 
            MainSplitView.IsPaneOpen ? Visibility.Collapsed : Visibility.Visible;
        SecondarySectionNameTextBlock.Opacity = MainSplitView.IsPaneOpen ? 0.0 : 1.0;
    }
}