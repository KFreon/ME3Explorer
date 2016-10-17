﻿using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UsefulThings.WPF;
using WPF_ME3Explorer.Textures;
using WPF_ME3Explorer.UI.ViewModels;
using UsefulThings;
using CSharpImageLibrary;
using System.Runtime.InteropServices;

namespace WPF_ME3Explorer.UI
{
    /// <summary>
    /// Interaction logic for Texplorer.xaml
    /// </summary>
    public partial class Texplorer : Window
    {
        public bool IsClosed { get; private set; }
        internal TexplorerViewModel vm = null;
        DragDropHandler<TreeTexInfo> TextureDragDropper = null;
        DragDropHandler<TexplorerTextureFolder> FolderDragDropper = null;
        static Random random = new Random();

        Action<System.Windows.Shell.TaskbarItemProgressState> TaskBarUpdater = null;


        public Texplorer()
        {
            InitializeComponent();
            vm = new TexplorerViewModel();

            TaskBarUpdater = new Action<System.Windows.Shell.TaskbarItemProgressState>(state => TaskBarProgressMeter.Dispatcher.Invoke(new Action(() => TaskBarProgressMeter.ProgressState = state)));

            vm.PropertyChanged += (sender, args) =>
            {
                // Change toolbar progress state when required.
                if (args.PropertyName == nameof(vm.Progress))
                    TaskBarUpdater(vm.Progress == 0 || vm.Progress == vm.MaxProgress ? System.Windows.Shell.TaskbarItemProgressState.None : System.Windows.Shell.TaskbarItemProgressState.Normal);
                else if (args.PropertyName == nameof(vm.ProgressIndeterminate))
                    TaskBarUpdater(vm.ProgressIndeterminate ? System.Windows.Shell.TaskbarItemProgressState.Indeterminate : TaskBarProgressMeter.ProgressState);
            };

            vm.ProgressCloser = new Action(() =>
            {
                HiderButton.Dispatcher.Invoke(() =>   // Not sure if this is necessary, but things aren't working otherwise.
                {
                    Storyboard closer = (Storyboard)HiderButton.Resources["ProgressPanelCloser"];
                    closer.Begin();
                });
            });

            vm.ProgressOpener = new Action(() =>
            {
                TreeScanBackground.Dispatcher.Invoke(() =>   // Not sure if this is necessary, but things aren't working otherwise.
                {
                    Storyboard closer = (Storyboard)TreeScanBackground.Resources["ProgressPanelOpener"];
                    closer.Begin();
                });
            });

            vm.TreePanelCloser = new Action(() =>
            {
                Storyboard closer = (Storyboard)TreeScanBackground.Resources["TreeScanClosePanelAnimation"];
                closer.Begin();
            });

            vm.TreePanelOpener = new Action(() =>
            {
                Storyboard opener = (Storyboard)SettingsButton.Resources["TreeScanPanelOpener"];
                opener.Begin();
            });

            DataContext = vm;
            GetBackgroundMovie();
            BackgroundMovie.Play();


            Action<TreeTexInfo, string[]> textureDropper = new Action<TreeTexInfo, string[]>((tex, files) => Task.Run(() => vm.ChangeTexture(tex, files[0]))); // Can only be one due to validation in DragOver

            var FolderDataGetter = new Func<TexplorerTextureFolder, Dictionary<string, Func<byte[]>>>(context =>
            {
                var SaveInformation = new Dictionary<string, Func<byte[]>>();
                for (int i = 0; i < context.TexturesInclSubs.Count; i++)
                {
                    var tex = context.TexturesInclSubs[i];
                    Func<byte[]> data = () =>  ToolsetTextureEngine.ExtractTexture(tex);
                    SaveInformation.Add(tex.DefaultSaveName, data);
                }
                return SaveInformation;
            });

            var TextureDataGetter = new Func<TreeTexInfo, Dictionary<string, Func<byte[]>>>(context => new Dictionary<string, Func<byte[]>> { { context.DefaultSaveName,
                    () =>
                    {
                        // KFreon: No conversions - need to use extract for that.
                        return ToolsetTextureEngine.ExtractTexture(context);
                    }
                } });

            Predicate<string[]> DropValidator = new Predicate<string[]>(files => files.Length == 1 && !files.Any(file => ImageFormats.ParseExtension(Path.GetExtension(file)) == ImageFormats.SupportedExtensions.UNKNOWN));

            TextureDragDropper = new DragDropHandler<TreeTexInfo>(this, textureDropper, DropValidator, TextureDataGetter);
            FolderDragDropper = new DragDropHandler<TexplorerTextureFolder>(this, null, null, FolderDataGetter);  // DropAction and Validator not required as TreeView not droppable.



            // As VM should be created before this constructor is called, can do this check now.
            if (vm.CurrentTree.Valid)
                vm.TreePanelCloser();
        }

        void GetBackgroundMovie()
        {
            Uri source = null;
            if (random.Next(6) == 5)   // Want this to be random and rarer
                source = new Uri("Resources/Normandy Arrival.mp4", UriKind.Relative);
            else
                source = new Uri("Resources/Basic relay.mp4", UriKind.Relative);

            if (source != BackgroundMovie.Source)
                BackgroundMovie.Source = source;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (vm.ChangedTextures.Count != 0)
            {
                if (MessageBox.Show("There are unsaved changes! Are you sure you want to quit?", "Forgetting something Commander?", MessageBoxButton.YesNo) == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }

            IsClosed = true;
        }

        private async void BeginScanButton_Click(object sender, RoutedEventArgs e)
        {
            await vm.BeginTreeScan();
        }

        private void MainListView_MouseDown(object sender, MouseButtonEventArgs e)
        {

            if (e.ClickCount > 1)
            {
                vm.ShowingPreview = true;

                // Async loading
                var texInfo = (TreeTexInfo)((FrameworkElement)sender).DataContext;
                Task.Run(() => vm.LoadPreview(texInfo));
            }
            else
            {
                var texInfo = (TreeTexInfo)((FrameworkElement)sender).DataContext;
                Task.Run(() => texInfo.PopulateDetails());
            }
        }

        private void PreviewPanel_MouseDown(object sender, MouseButtonEventArgs e)
        {
            vm.ShowingPreview = false;
            vm.PreviewImage = null;
        }

        private async void SearchResultsItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Select folder
            TreeTexInfo tex = (TreeTexInfo)((FrameworkElement)sender).DataContext;
            TexplorerTextureFolder treeFolder = vm.CurrentTree.AllFolders.FirstOrDefault(folder => folder.Textures.Contains(tex));
            vm.SelectedFolder = treeFolder;

            // Select texture in folder
            // Find VirtualizingWrapPanel and ensure item is in view
            while (MainDisplayPanel.ItemContainerGenerator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                await Task.Delay(100);

            await Task.Delay(100);
            var container = MainDisplayPanel.ItemContainerGenerator.ContainerFromIndex(0);  // First visual that exists
            var current = VisualTreeHelper.GetParent(container);
            while (current != null)
            {
                if ((current as VirtualizingWrapPanel) != null)
                    break;
                
                current = VisualTreeHelper.GetParent(current);
            }

            VirtualizingWrapPanel wrapper = current as VirtualizingWrapPanel;
            wrapper.BringItemIntoView(tex);

            // Select item
            vm.SelectedTexture = tex;
            Task.Run(() => tex.PopulateDetails());
        }


        DispatcherTimer TreeSearchResetTimer = null;
        StringBuilder TreeSearchMemory = null;
        private void MainTreeView_KeyDown(object sender, KeyEventArgs e)
        {
            // Only want letters in here.
            char key = e.Key.ToString()[0];
            if (!Char.IsLetter(key))
                return;

            if (TreeSearchMemory == null)
            {
                // Setup memory and timer
                TreeSearchMemory = new StringBuilder();
                
                if (TreeSearchResetTimer == null)
                {
                    TreeSearchResetTimer = new DispatcherTimer();
                    TreeSearchResetTimer.Interval = TimeSpan.FromSeconds(3);  // Waits x seconds before forgetting previous search
                    TreeSearchResetTimer.Tick += (unused1, unused2) =>
                    {
                        TreeSearchMemory = null;
                        TreeSearchResetTimer.Stop();
                    };
                }

                TreeSearchResetTimer.Start();
            }

            TreeSearchMemory.Append(key);  // Add to memory

            // Reset timer interval
            TreeSearchResetTimer.Stop();
            TreeSearchResetTimer.Start();


            // Peform search over top level folders only
            var topFolders = vm.CurrentTree.TextureFolders[0].Folders;
            string temp = TreeSearchMemory.ToString();
            foreach (var folder in topFolders)
                if (folder.Name.StartsWith(temp, StringComparison.OrdinalIgnoreCase))
                {
                    vm.SelectedFolder = folder;
                    break;
                }


            PreviewPanel_MouseDown(null, null);
            e.Handled = true;
        }

        private void ImportTreeButton_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Title = "Select tree to import";
            sfd.Filter = "All Supported | *.tree;*.bin|Toolset trees | *.tree|Binary files | *.bin";
            sfd.FileName = Path.GetFileName(vm.CurrentTree.TreePath);
            if (sfd.ShowDialog() == true)
                File.Copy(sfd.FileName, vm.CurrentTree.TreePath);
        }

        private void ExportTreeButton_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Title = "Select destination for current tree";
            sfd.Filter = "Toolset trees | *.tree";
            sfd.FileName = Path.GetFileName(vm.CurrentTree.TreePath);
            if (sfd.ShowDialog() == true)
                File.Copy(vm.CurrentTree.TreePath, sfd.FileName);
        }

        private void ExportCSVButton_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "Comma Separated File|*.CSV";
            if (sfd.ShowDialog() == true)
                vm.CurrentTree.ExportToCSV(sfd.FileName, true);
        }

        private void ReallyDeleteTreeButton_Click(object sender, RoutedEventArgs e)
        {
            vm.DeleteCurrentTree();
        }

        private void RegenerateTopMenu_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(async () => await vm.RegenerateThumbs());
        }

        private void BackgroundMovie_MediaEnded(object sender, RoutedEventArgs e)
        {
            BackgroundMovie.Stop();
            GetBackgroundMovie();
            BackgroundMovie.Play();
        }

        private void BackgroundMovie_MediaOpened(object sender, RoutedEventArgs e)
        {
            BackgroundMovie.Play();
        }

        private void BackgroundMovie_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            Console.WriteLine();
        }

        private void TexplorerWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)   // Catch Ctrl + F
            {
                SearchBox.Focus();
                Keyboard.Focus(SearchBox);
                e.Handled = true;
            }
        }

        private void MainDisplayPanel_Drop(object sender, DragEventArgs e)
        {
            MainListView_DragLeave(sender, null);
            TextureDragDropper.Drop(sender, e);
        }

        private void MainListView_MouseMove(object sender, MouseEventArgs e)
        {
            TextureDragDropper.MouseMove(sender, e);
        }

        private void MainListView_DragOver(object sender, DragEventArgs e)
        {
            TextureDragDropper.DragOver(e);
        }

        private void MainTreeView_MouseMove(object sender, MouseEventArgs e)
        {
            FolderDragDropper.MouseMove(sender, e);
        }

        private void SavePCCsListButton_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.FileName = $"{Path.GetFileNameWithoutExtension(vm.SelectedTexture.DefaultSaveName)}_PCCs-ExpIDs.csv";
            sfd.Filter = "Comma Separated | *.csv";
            sfd.Title = "Select destination for CSV";
            if (sfd.ShowDialog() == true)
                vm.ExportSelectedTexturePCCList(sfd.FileName);
        }

        private void MainListView_DragEnter(object sender, DragEventArgs e)
        {
            // Show border when dragging into tile.
            var border = sender as Border;
            border.BorderBrush = new SolidColorBrush(Colors.Red);
        }

        private void MainListView_DragLeave(object sender, DragEventArgs e)
        {
            // Remove border when leaving tile.
            var border = sender as Border;
            border.BorderBrush = new SolidColorBrush(Colors.Transparent);
        }

        private void MainTreeView_MouseDown(object sender, MouseButtonEventArgs e)
        {
            PreviewPanel_MouseDown(null, null); // Ensure preview is closed
        }

        private void Extract_BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = vm.GetFullDialogFilters();
            sfd.FileName = vm.SelectedTexture?.DefaultSaveName;
            sfd.Title = "Select destination for extracted texture";

            if (sfd.ShowDialog() == true)
                vm.Extract_SavePath = sfd.FileName;
        }

        private void Extract_ExtractButton_Click(object sender, RoutedEventArgs e)
        {
            vm.ExtractTexture(vm.SelectedTexture, vm.Extract_SavePath, vm.Extract_BuildMips, vm.Extract_SaveFormat);
        }

        private void Change_BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = vm.GetFullDialogFilters();
            ofd.Title = "Select image to replace";

            if (ofd.ShowDialog() == true)
                vm.Change_SavePath = ofd.FileName;
        }
    }
}
