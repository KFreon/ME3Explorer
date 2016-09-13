﻿#define ThreadedScan
#undef ThreadedScan

using CSharpImageLibrary;
using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using UsefulThings;
using UsefulThings.WPF;
using WPF_ME3Explorer.Debugging;
using WPF_ME3Explorer.PCCObjectsAndBits;
using WPF_ME3Explorer.Textures;


namespace WPF_ME3Explorer.UI.ViewModels
{

    public class TexplorerViewModel : MEViewModelBase<TreeTexInfo>
    {
        #region Commands
        CommandHandler showGameInfo = null;
        public CommandHandler ShowGameInfoCommand
        {
            get
            {
                if (showGameInfo == null)
                    showGameInfo = new CommandHandler(new Action<object>(param =>
                        {
                            int version = int.Parse((string)param);
                            GameInformation info = new GameInformation(version);
                            info.Closed += (unused1, unused2) => GameDirecs.RefreshListeners();  // Refresh all game directory related info once window is closed. 
                            info.Show();
                        }));

                return showGameInfo;
            }
        }

        CommandHandler saveChangesCommand = null;
        public CommandHandler SaveChangesCommand
        {
            get
            {
                if (saveChangesCommand == null)
                    saveChangesCommand = new CommandHandler(new Action<object>(async param =>
                    {
                        Busy = true;

                        var texes = ChangedTextures.ToArray();

                        // Show progress panel
                        if (texes.Length > 5)
                            ProgressOpener();
                        else
                            ProgressIndeterminate = true;

                        // Install changed textures
                        await ToolsetTextureEngine.InstallTextures(NumThreads, this, GameDirecs, cts, texes);

                        MaxProgress = Progress;
                        Status = $"Saved all files!";


                        ThumbnailWriter.BeginAdding();

                        // Clear ChangedTextures
                        foreach (var tex in ChangedTextures)
                        {
                            tex.HasChanged = false;
                            tex.Thumb = ThumbnailWriter.ReplaceOrAdd(tex.Thumb.ChangedThumb, tex.Thumb);
                        }
                        ChangedTextures.Clear();

                        // Update tree
                        CurrentTree.SaveToFile();

                        // Close progress
                        if (texes.Length > 5)
                            ProgressCloser();
                        else
                            ProgressIndeterminate = false;

                        Busy = false;
                    }));

                return saveChangesCommand;
            }
        }

        CommandHandler changeTree = null;
        public CommandHandler ChangeTreeCommand
        {
            get
            {
                if (changeTree == null)
                    changeTree = new CommandHandler(new Action<object>(param =>
                    {
                        if (ChangedTextures.Count > 0)
                        {
                            var result = MessageBox.Show("There are unsaved changes. Do you want to continue to change trees? Continuing will NOT save changes made.", "You sure about this, Shepard?", MessageBoxButton.YesNo);
                            if (result == MessageBoxResult.No)
                                return;
                        }
                        int version = ((TreeDB)param).GameVersion;
                        ChangeSelectedTree(version);
                        LoadFTSandTree();
                    }));

                return changeTree;
            }
        }

        CommandHandler startTPFToolsModeCommand = null;
        public CommandHandler StartTPFToolsModeCommand
        {
            get
            {
                if (startTPFToolsModeCommand == null)
                {
                    startTPFToolsModeCommand = new CommandHandler(() =>
                    {
                        // Start TPFTools
                        var tpftools = ToolsetInfo.TPFToolsInstance;

                        // Open window if necessary, minimising it either way.
                        tpftools.WindowState = WindowState.Minimized;
                        if (tpftools.Visibility != Visibility.Visible)
                            tpftools.Show();

                        // Change mode indicator
                        TPFToolsModeEnabled = true;
                    });
                }

                return startTPFToolsModeCommand;
            }
        }

        CommandHandler stopTPFToolsModeCommand = null;
        public CommandHandler StopTPFToolsModeCommand
        {
            get
            {
                if (stopTPFToolsModeCommand == null)
                {
                    stopTPFToolsModeCommand = new CommandHandler(() =>
                    {
                        // Start TPFTools
                        var tpftools = ToolsetInfo.TPFToolsInstance;

                        // Show TPFTools window
                        tpftools.WindowState = WindowState.Normal;
                        tpftools.Activate();


                        // Change mode indicator
                        TPFToolsModeEnabled = false;
                    });
                }

                return stopTPFToolsModeCommand;
            }
        }
        #endregion Commands

        

        #region UI Actions
        public Action TreePanelCloser = null;
        public Action ProgressOpener = null;
        public Action TreePanelOpener = null;
        public Action ProgressCloser = null;
        #endregion UI Actions

        #region Properties

        List<DLCEntry> FTSDLCs { get; set; } = new List<DLCEntry>();
        List<GameFileEntry> FTSGameFiles { get; set; } = new List<GameFileEntry>();
        List<AbstractFileEntry> FTSExclusions { get; set; } = new List<AbstractFileEntry>();
        public ICollectionView DLCItemsView { get; set; }
        public ICollectionView ExclusionsItemsView { get; set; }
        public ICollectionView FileItemsView { get; set; }
        ThumbnailWriter ThumbnailWriter = null;
        public MTRangedObservableCollection<string> Errors { get; set; } = new MTRangedObservableCollection<string>();
        public MTRangedObservableCollection<TexplorerTextureFolder> TextureFolders { get; set; } = new MTRangedObservableCollection<TexplorerTextureFolder>();
        public MTRangedObservableCollection<TexplorerTextureFolder> AllFolders { get; set; } = new MTRangedObservableCollection<TexplorerTextureFolder>();
        public MTRangedObservableCollection<TreeTexInfo> ChangedTextures { get; set; } = new MTRangedObservableCollection<TreeTexInfo>();

        public bool TPFToolsModeEnabled
        {
            get
            {
                return ToolsetTextureEngine.TPFToolsModeEnabled;
            }
            set
            {
                SetProperty(ref ToolsetTextureEngine.TPFToolsModeEnabled, value);
            }
        }

        public bool? PCCsCheckAll
        {
            get
            {
                if (SelectedTexture?.PCCS == null)
                    return false;

                int num = SelectedTexture.PCCS.Where(pcc => pcc.IsChecked).Count();
                if (num == 0)
                    return false;
                else if (num == SelectedTexture.PCCS.Count)
                    return true;

                return null;
            }
            set
            {
                SelectedTexture.PCCS.AsParallel().ForAll(pcc => pcc.IsChecked = value == true);
            }
        }

        TexplorerTextureFolder mySelected = null;
        public TexplorerTextureFolder SelectedFolder
        {
            get
            {
                return mySelected;
            }
            set
            {
                SetProperty(ref mySelected, value);
            }
        }

        TreeTexInfo selectedTexture = null;
        public TreeTexInfo SelectedTexture
        {
            get
            {
                return selectedTexture;
            }
            set
            {
                SetProperty(ref selectedTexture, value);
                OnPropertyChanged(nameof(PCCsCheckAll));
            }
        }

        string ftsFilesSearch = null;
        public string FTSFilesSearch
        {
            get
            {
                return ftsFilesSearch;
            }
            set
            {
                SetProperty(ref ftsFilesSearch, value);
                FileItemsView.Refresh();
            }
        }

        string ftsExclusionsSearch = null;
        public string FTSExclusionsSearch
        {
            get
            {
                return ftsExclusionsSearch;
            }
            set
            {
                SetProperty(ref ftsExclusionsSearch, value);
                ExclusionsItemsView.Refresh();
            }
        }
        

        bool showingPreview = false;
        public bool ShowingPreview
        {
            get
            {
                return showingPreview;
            }
            set
            {
                SetProperty(ref showingPreview, value);
            }
        }

        BitmapSource previewImage = null;
        public BitmapSource PreviewImage
        {
            get
            {
                return previewImage;
            }
            set
            {
                SetProperty(ref previewImage, value);
            }
        }

        bool ftsReady = false;
        

        public bool FTSReady
        {
            get
            {
                return ftsReady;
            }
            set
            {
                SetProperty(ref ftsReady, value);
            }
        }

        #endregion Properties

        public override void ChangeSelectedTree(int game)
        {
            Status = $"Selected Tree changed from {GameVersion} to {game}.";

            base.ChangeSelectedTree(game);

            RefreshTreeRelatedProperties();
        }

        public TexplorerViewModel() : base()
        {
            DebugOutput.StartDebugger("Texplorer");

            #region FTS Filtering
            DLCItemsView = CollectionViewSource.GetDefaultView(FTSDLCs);
            DLCItemsView.Filter = item => !((DLCEntry)item).IsChecked;

            FileItemsView = CollectionViewSource.GetDefaultView(FTSGameFiles);
            FileItemsView.Filter = item =>
            {
                GameFileEntry entry = (GameFileEntry)item;
                return !entry.IsChecked && !entry.FilterOut && 
                    (String.IsNullOrEmpty(FTSFilesSearch) ? true : 
                    entry.Name.Contains(FTSFilesSearch, StringComparison.OrdinalIgnoreCase) || entry.FilePath?.Contains(FTSFilesSearch, StringComparison.OrdinalIgnoreCase) == true);
            };

            ExclusionsItemsView = CollectionViewSource.GetDefaultView(FTSExclusions);
            ExclusionsItemsView.Filter = item =>
            {
                AbstractFileEntry entry = (AbstractFileEntry)item;
                return entry.IsChecked && ((entry as GameFileEntry)?.FilterOut != true) && 
                    (String.IsNullOrEmpty(FTSExclusionsSearch) ? true : 
                    entry.Name.Contains(FTSExclusionsSearch, StringComparison.OrdinalIgnoreCase) || entry.FilePath?.Contains(FTSExclusionsSearch, StringComparison.OrdinalIgnoreCase) == true);
            };
            #endregion FTS Filtering


            #region Setup Texture UI Commands
            TreeTexInfo.ChangeCommand = new CommandHandler(new Action<object>(tex =>
            {
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.Filter = "DirectX Images|*.dds";  // TODO Expand to allow any ImageEngine supported format for on the fly conversion. Need to have some kind of preview first though to maybe change the conversion parameters.
                if (ofd.ShowDialog() != true)
                    return;

                Task.Run(() => ChangeTexture((TreeTexInfo)tex, ofd.FileName));
            }));

            TreeTexInfo.ExtractCommand = new CommandHandler(new Action<object>(tex =>
            {
                SaveFileDialog sfd = new SaveFileDialog();
                var texture = tex as TreeTexInfo;
                sfd.FileName = texture.DefaultSaveName;
                sfd.Filter = "DirectX Images|*.dds";  // TODO Expand to allow any ImageEngine supported format.
                if (sfd.ShowDialog() != true)
                    return;

                ExtractTexture((TreeTexInfo)tex, sfd.FileName);
            }));

            TreeTexInfo.LowResFixCommand = new CommandHandler(new Action<object>(tex =>
            {
                ME1_LowResFix((TreeTexInfo)tex);
            }));

            TreeTexInfo.RegenerateThumbCommand = new CommandHandler(new Action<object>(async tex =>
            {
                await Task.Run(async () => await RegenerateThumbs((TreeTexInfo)tex)).ConfigureAwait(false);
            }));

            TexplorerTextureFolder.RegenerateThumbsDelegate = RegenerateThumbs;

            #endregion Setup UI Commands

            GameDirecs.GameVersion = Properties.Settings.Default.TexplorerGameVersion;
            OnPropertyChanged(nameof(GameVersion));

            // Setup thumbnail writer - not used unless tree scanning.
            ThumbnailWriter = new ThumbnailWriter(GameDirecs);
            BeginTreeLoading();
        }

        internal async Task RegenerateThumbs(params TreeTexInfo[] textures)
        {
            Busy = true;
            StartTime = Environment.TickCount;
            List<TreeTexInfo> texes = new List<TreeTexInfo>();

            // No args = regen everything
            if (textures?.Length < 1)
                texes.AddRange(Textures);
            else
                texes.AddRange(textures);

            // Open Progress Panel if worth it.
            if (texes.Count > 10)
                ProgressOpener();

            DebugOutput.PrintLn($"Regenerating {texes.Count} thumbnails...");

            MaxProgress = texes.Count;
            Progress = 0;

            ThumbnailWriter.BeginAdding();
            int errors = 0;

            #region Setup Regen Pipeline
            // Get all PCCs - maybe same as saving? - only need first pcc of each texture
            BufferBlock<Tuple<PCCObject, IGrouping<string, TreeTexInfo>>> pccBuffer = new BufferBlock<Tuple<PCCObject, IGrouping<string, TreeTexInfo>>>();
            
            // loop over textures getting a tex2D from each tex located in pcc
            var tex2DMaker = new TransformBlock<Tuple<PCCObject, IGrouping<string, TreeTexInfo>>, Tuple<Thumbnail, Texture2D>>(obj =>
            {
                TreeTexInfo tex = obj.Item2.First();
                Texture2D tex2D = new Texture2D(obj.Item1, tex.PCCS[0].ExpID, GameDirecs);
                return new Tuple<Thumbnail, Texture2D>(tex.Thumb, tex2D);
            });

            // Get thumb from each tex2D
            var ThumbMaker = new TransformBlock<Tuple<Thumbnail, Texture2D>, Tuple<Thumbnail, MemoryStream>>(bits => 
            {
                return new Tuple<Thumbnail, MemoryStream>(bits.Item1, ToolsetTextureEngine.GetThumbFromTex2D(bits.Item2, 128));
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = NumThreads, BoundedCapacity = NumThreads });

            // Write thumb to disk
            var WriteThumbs = new ActionBlock<Tuple<Thumbnail, MemoryStream>>(bits => ThumbnailWriter.ReplaceOrAdd(bits.Item2, bits.Item1), new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = NumThreads, BoundedCapacity = NumThreads });

            // Create pipeline
            pccBuffer.LinkTo(tex2DMaker, new DataflowLinkOptions { PropagateCompletion = true });
            tex2DMaker.LinkTo(ThumbMaker, new DataflowLinkOptions { PropagateCompletion = true });
            ThumbMaker.LinkTo(WriteThumbs, new DataflowLinkOptions { PropagateCompletion = true });

            // Start producer
            PCCRegenProducer(pccBuffer, texes);
            #endregion Setup Regen Pipeline

            await WriteThumbs.Completion;  // Wait for pipeline to complete

            if (cts.IsCancellationRequested)
            {
                ThumbnailWriter.FinishAdding(); // Close thumbnail writer gracefully
                Status = "Thumbnail Regeneration was cancelled!";
            }
            else
                Status = $"Regenerated {texes.Count - errors} thumbnails" + (errors == 0 ? "." : $" with {errors} errors.");

            Progress = MaxProgress;

            ThumbnailWriter.FinishAdding();

            // Close Progress Panel if previously opened.
            if (texes.Count > 10)
                ProgressCloser();

            StartTime = 0;
            Busy = false;
        }

        async Task PCCRegenProducer(BufferBlock<Tuple<PCCObject, IGrouping<string, TreeTexInfo>>> pccBuffer, List<TreeTexInfo> texes)
        {
            // Gets all distinct pcc's being altered.
            var pccTexGroups =
                from tex in texes
                group tex by tex.PCCS[0].Name;

            // Send each unique PCC to get textures saved to it.
            foreach (var texGroup in pccTexGroups)
            {
                if (cts.IsCancellationRequested)
                    break;

                string pcc = texGroup.Key;
                PCCObject pccobj = new PCCObject(pcc, GameVersion);
                await pccBuffer.SendAsync(new Tuple<PCCObject, IGrouping<string, TreeTexInfo>>(pccobj, texGroup));
            }

            pccBuffer.Complete();
        }

        async void BeginTreeLoading()
        {
            Busy = true;
            Status = "Loading Trees...";

            AbstractFileEntry.Updater = new Action(() => UpdateFTS());

            await Task.Run(() =>
            {
                // Load all three trees
                base.LoadTrees();

                /// Can take a long time if disk is busy
                // KFreon: Populate game files info with tree info
                if (GameDirecs.Files?.Count <= 0)
                {
                    DebugOutput.PrintLn($"Game files not found for ME{GameDirecs.GameVersion} at {GameDirecs.PathBIOGame}");
                    Status = "Game Files not found!";
                    Busy = false;
                    return;
                }
            });

            await LoadFTSandTree();

            ToolsetInfo.SetupDiskCounters(Path.GetPathRoot(GameDirecs.BasePath).TrimEnd('\\'));

            Status = CurrentTree.Valid ? "Ready!" : Status; 
            Busy = false;
        }

        async Task LoadFTSandTree(bool panelAlreadyOpen = false)
        {
            FTSReady = false;

            // Clear everything to start again.
            FTSExclusions.Clear();
            FTSGameFiles.Clear();
            FTSDLCs.Clear();

            // Tree isn't valid. Open the panel immediately.
            if (!panelAlreadyOpen && !CurrentTree.Valid)
                TreePanelOpener();

            await Task.Run(() =>
            {
                // Get DLC's
                DLCEntry basegame = new DLCEntry("BaseGame", GameDirecs.Files.Where(file => !file.Contains(@"DLC\DLC_") && !file.EndsWith(".tfc", StringComparison.OrdinalIgnoreCase)).ToList());
                FTSDLCs.Add(basegame);
                GetDLCEntries();
            });

            // Add all DLC files to global files list
            foreach (DLCEntry dlc in FTSDLCs)
                FTSGameFiles.AddRange(dlc.Files);

            if (CurrentTree.Valid)
            {
                await LoadValidTree();

                /* Find any existing exclusions from when tree was created.*/
                // Set excluded DLC's checked first
                FTSDLCs.ForEach(dlc => dlc.IsChecked = !dlc.Files.Any(file => CurrentTree.ScannedPCCs.Contains(file.FilePath)));

                // Then set all remaining exlusions
                foreach (DLCEntry dlc in FTSDLCs.Where(dlc => !dlc.IsChecked))
                    dlc.Files.ForEach(file => file.IsChecked = !CurrentTree.ScannedPCCs.Contains(file.FilePath));
            }
            else
                Status = "Tree invalid/non-existent. Begin Tree Scan by clicking 'Settings'";

            FTSExclusions.AddRange(FTSDLCs);
            FTSExclusions.AddRange(FTSGameFiles);

            FTSReady = true;
            UpdateFTS();
        }

        async Task LoadValidTree()
        {
            // Put away TreeScan Panel since it isn't required if tree is valid.
            if (TreePanelCloser != null)
                TreePanelCloser();

            CurrentTree.IsSelected = true;

            await Task.Run(() => ConstructTree());
        }

        public void UpdateFTS()
        {
            DLCItemsView.Refresh();
            ExclusionsItemsView.Refresh();
            FileItemsView.Refresh();
        }

        void GetDLCEntries()
        {
            List<string> DLCs = Directory.EnumerateDirectories(GameDirecs.DLCPath).Where(direc => !direc.Contains("_metadata")).ToList();
            foreach(string dlc in DLCs)
            {
                string[] parts = dlc.Split('\\');
                string DLCName = parts.First(part => part.Contains("DLC_"));

                string name = MEDirectories.MEDirectories.GetCommonDLCName(DLCName);
                DLCEntry entry = new DLCEntry(name, GameDirecs.Files.Where(file => file.Contains(DLCName) && !file.EndsWith(".tfc", StringComparison.OrdinalIgnoreCase)).ToList());

                FTSDLCs.Add(entry);
            }
        }

        internal async Task BeginTreeScan()
        {
            Busy = true;

            TextureSearch = null;

            DebugOutput.PrintLn($"Beginning Tree scan for ME{GameVersion}.");

            // Populate Tree PCCs in light of exclusions
            foreach (GameFileEntry item in FTSGameFiles.Where(file => !file.IsChecked && !file.FilterOut))
                CurrentTree.ScannedPCCs.Add(item.FilePath);

            DebugOutput.PrintLn("Attempting to delete old thumbnails.");

            // Remove any existing thumbnails
            if (File.Exists(GameDirecs.ThumbnailCachePath))
                File.Delete(GameDirecs.ThumbnailCachePath);

            // Wait until file properly deleted
            while (File.Exists(GameDirecs.ThumbnailCachePath))
                await Task.Delay(100); 

            StartTime = Environment.TickCount;

            ThumbnailWriter.BeginAdding();

            await BeginScanningPCCs();

            // Reorder ME2 Game files - DISABLED FOR NOW - Think it should be in the loader for Texture2D. Think I was hoping I could intialise things with this.
            if (GameVersion == 2)
            {
                DebugOutput.PrintLn("Reordering ME2 textures...");
                //await Task.Run(() => Parallel.ForEach(CurrentTree.Textures, tex => tex.ReorderME2Files())).ConfigureAwait(false);  // This should be fairly quick so let the runtime deal with threading.
            }

            StartTime = 0; // Stop Elapsed Time from counting
            ThumbnailWriter.FinishAdding();

            if (cts.IsCancellationRequested)
            {
                Busy = false;
                return;
            }

            DebugOutput.PrintLn("Saving tree to disk...");
            await Task.Run(() => CurrentTree.SaveToFile()).ConfigureAwait(false);
            DebugOutput.PrintLn($"Treescan completed. Elapsed time: {ElapsedTime}. Num Textures: {CurrentTree.Textures.Count}.");
            await Task.Run(() => ConstructTree()).ConfigureAwait(false);
            CurrentTree.Valid = true; // It was just scanned after all.

            // Put away TreeScanProgress Window
            ProgressCloser();
            GC.Collect();  // On a high RAM x64 system, things sit around at like 6gb. Might as well clear it.

            Busy = false;
        }

        async Task<KeyValuePair<string, MemoryStream>> LoadTFC(string tfc)
        {
            using (FileStream fs = new FileStream(tfc, FileMode.Open, FileAccess.Read, FileShare.None, 4096, true))
            {
                //MemoryStream ms = RecyclableMemoryManager.GetStream((int)fs.Length);
                MemoryStream ms = new MemoryStream((int)fs.Length);
                await fs.CopyToAsync(ms).ConfigureAwait(false);
                return new KeyValuePair<string, MemoryStream>(tfc, ms);
            }
        }

        /// <summary>
        /// Scans PCCs in Tree or given pccs e.g from adding textures to existing tree.
        /// </summary>
        /// <param name="pccs">PCCs to scan (to add to existing tree)</param>
        async Task BeginScanningPCCs(List<string> pccs = null)
        {
            Progress = 0;

            // DEBUGGING
            /*CurrentTree.ScannedPCCs.Clear();
            CurrentTree.ScannedPCCs.Add(@"R:\Games\Mass Effect\BioGame\CookedPC\Packages\GameObjects\Characters\Humanoids\Salarian\BIOG_SAL_HED_PROMorph_R.upk");*/

            IList<string> PCCsToScan = CurrentTree.ScannedPCCs;  // Can't use ?? here as ScannedPCCs and pccs are different classes.
            if (pccs != null)
                PCCsToScan = pccs;

            MaxProgress = PCCsToScan.Count;

            //ME3 only
            Dictionary<string, MemoryStream> TFCs = null;
            if (GameVersion != 1)
            {
                Status = "Reading TFC's into memory...";

                // Read TFCs into RAM if available/requested.
                double RAMinGB = ToolsetInfo.AvailableRam / 1024d / 1024d / 1024d;
                if (Environment.Is64BitProcess && RAMinGB > 10)
                {
                    // Enough RAM to load TFCs
                    TFCs = new Dictionary<string, MemoryStream>();

                    var tfcfiles = GameDirecs.Files.Where(tfc => tfc.EndsWith("tfc"));
                    foreach (var tfc in tfcfiles)
                    {
                        var item = await LoadTFC(tfc);
                        TFCs.Add(item.Key, item.Value);
                    }
                }
            }

            Status = "Beginning Tree Scan...";

            

            // Perform scan
            await ScanAllPCCs(PCCsToScan, TFCs).ConfigureAwait(false);   // Start entire thing on another thread which awaits when collection is full, then wait for pipeline completion.

            // Re-arrange files
            if (GameVersion == 1)
                ToolsetTextureEngine.ME1_SortTexturesPCCs(CurrentTree.Textures);

            // Dispose all TFCs
            if (TFCs != null)
            {
                foreach (var tfc in TFCs)
                    tfc.Value.Dispose();

                TFCs.Clear();
                TFCs = null;
            }
                
            Debug.WriteLine($"Max ram during scan: {Process.GetCurrentProcess().PeakWorkingSet64 / 1024d / 1024d / 1024d}");
            DebugOutput.PrintLn($"Max ram during scan: {Process.GetCurrentProcess().PeakWorkingSet64 / 1024d / 1024d / 1024d}");

            Progress = MaxProgress;

            if (cts.IsCancellationRequested)
                Status = "Tree scan was cancelled!";
            else
                Status = $"Scan complete. Found {CurrentTree.Textures.Count} textures. Elapsed scan time: {ElapsedTime}.";
        }

        Task ScanAllPCCs(IList<string> PCCs, Dictionary<string, MemoryStream> TFCs)
        {
            // Parallel scanning
            int bound = 10;
            double RAMinGB = ToolsetInfo.AvailableRam / 1024d / 1024d / 1024d;
            if (RAMinGB < 10)
                bound = 5;

            // Create buffer to store PCCObjects from disk
            BufferBlock<PCCObject> pccScanBuffer = new BufferBlock<PCCObject>(new DataflowBlockOptions { BoundedCapacity = bound });   // Collection can't grow past this. Good for low RAM situations.

            // Decide degrees of parallelism for each block of the pipeline
            int numScanners = NumThreads;
            int maxParallelForSorter = NumThreads;
#if (ThreadedScan)
            maxParallelForSorter = 1;
            numScanners = 1;
#endif


            var texSorterOptions = new ExecutionDataflowBlockOptions { BoundedCapacity = maxParallelForSorter, MaxDegreeOfParallelism = maxParallelForSorter };
            var pccScannerOptions = new ExecutionDataflowBlockOptions { BoundedCapacity = numScanners, MaxDegreeOfParallelism = numScanners };

            // Setup pipeline blocks
            var pccScanner = new TransformManyBlock<PCCObject, TreeTexInfo>(pcc => ScanSinglePCC(pcc, TFCs), pccScannerOptions);
            var texSorter = new ActionBlock<TreeTexInfo>(tex => CurrentTree.AddTexture(tex), texSorterOptions);  // In another block so as to allow Generating Thumbnails to decouple from identifying textures

            // Link together
            pccScanBuffer.LinkTo(pccScanner, new DataflowLinkOptions { PropagateCompletion = true });
            pccScanner.LinkTo(texSorter, new DataflowLinkOptions { PropagateCompletion = true });

            // Begin producer
            PCCProducer(PCCs, pccScanBuffer);

            // Return task to await for pipeline completion - only need to wait for last block as PropagateCompletion is set.
            return texSorter.Completion;
        }

        async Task PCCProducer(IList<string> PCCs, BufferBlock<PCCObject> pccs)
        {
            for (int i = 0; i < PCCs.Count; i++)
            {
                if (cts.IsCancellationRequested)
                    break;

                string file = PCCs[i];
                PCCObject pcc = await PCCObject.CreateAsync(file, GameVersion);

                await pccs.SendAsync(pcc);
            }

            pccs.Complete();
        }

        List<TreeTexInfo> ScanSinglePCC(PCCObject pcc, Dictionary<string, MemoryStream> TFCs)
        {
            List<TreeTexInfo> texes = new List<TreeTexInfo>();
            DebugOutput.PrintLn($"Scanning: {pcc.pccFileName}");

            try
            {
                for (int i = 0; i < pcc.Exports.Count; i++)
                {
                    ExportEntry export = pcc.Exports[i];
                    if (!export.ValidTextureClass())
                        continue;

                    Texture2D tex2D = null;
                    try
                    {
                        tex2D = new Texture2D(pcc, i, GameDirecs);
                    }
                    catch (Exception e)
                    {
                        Errors.Add(e.ToString());
                        continue;
                    }

                    // Skip if no images
                    if (tex2D.ImageList.Count == 0)
                    {
                        tex2D.Dispose();
                        continue;
                    }

                    try
                    {
                        TreeTexInfo info = new TreeTexInfo(tex2D, ThumbnailWriter, export, TFCs, Errors, GameDirecs);
                        texes.Add(info);
                    }
                    catch(Exception e)
                    {
                        Errors.Add(e.ToString());
                    }
                }
            }
            catch (Exception e)
            {
                DebugOutput.PrintLn($"Scanning failed on {pcc.pccFileName}. Reason: {e.ToString()}.");
            }
            finally
            {
                pcc.Dispose();
            }

            Progress++;
            Status = $"Scanning PCC's to build tree: {Progress} / {MaxProgress}";

            return texes;       
        }

        void ConstructTree()
        {
            Busy = true;
            DebugOutput.PrintLn("Constructing Tree...");

            // Top all encompassing node
            TexplorerTextureFolder TopTextureFolder = new TexplorerTextureFolder("All Texture Files", null, null);

            // Normal nodes
            foreach (var tex in CurrentTree.Textures)
                RecursivelyCreateFolders(tex.FullPackage, "", TopTextureFolder, tex);

            Console.WriteLine($"Total number of folders: {AllFolders.Count}");
            // Alphabetical order
            TopTextureFolder.Folders = new MTRangedObservableCollection<TexplorerTextureFolder>(TopTextureFolder.Folders.OrderBy(p => p));

            TextureFolders.Add(TopTextureFolder);  // Only one item in this list. Chuckles.

            // Add textures to base class list - mostly just for searching.
            Textures.Clear();
            Textures.AddRange(CurrentTree.Textures);

            // Add checkbox listener for linking the check action of individual pccs to top level Check All
            foreach (var tex in Textures)
                foreach (var pccentry in tex.PCCS)
                    pccentry.PropertyChanged += (source, args) =>
                      {
                          if (args.PropertyName == nameof(pccentry.IsChecked))
                              OnPropertyChanged(nameof(PCCsCheckAll));
                      };

            DebugOutput.PrintLn("Tree Constructed!");
            Busy = false;
        }

        void RecursivelyCreateFolders(string package, string oldFilter, TexplorerTextureFolder topFolder, TreeTexInfo texture)
        {
            int dotInd = package.IndexOf('.') + 1;
            string name = package;
            if (dotInd != 0)
                name = package.Substring(0, dotInd).Trim('.');

            string filter = oldFilter + '.' + name;
            filter = filter.Trim('.');

            TexplorerTextureFolder newFolder = new TexplorerTextureFolder(name, filter, topFolder);

            // Add texture if part of this folder
            if (newFolder.Filter == texture.FullPackage)
                newFolder.Textures.Add(texture);

            TexplorerTextureFolder existingFolder = topFolder.Folders.FirstOrDefault(folder => newFolder.Name == folder.Name);
            if (existingFolder == null)  // newFolder not found in existing folders
            {
                topFolder.Folders.Add(newFolder);
                AllFolders.Add(newFolder);

                // No more folders in package
                if (dotInd == 0)
                    return;

                string newPackage = package.Substring(dotInd).Trim('.');
                RecursivelyCreateFolders(newPackage, filter, newFolder, texture);
            }
            else
            {  // No subfolders for newFolder yet, need to make them if there are any

                // Add texture if necessary
                if (existingFolder.Filter == texture.FullPackage)
                    existingFolder.Textures.Add(texture);

                // No more folders in package
                if (dotInd == 0)
                    return;

                string newPackage = package.Substring(dotInd).Trim('.');
                RecursivelyCreateFolders(newPackage, filter, existingFolder, texture);
            }
        }

        internal void LoadPreview(TreeTexInfo texInfo)
        {
            using (PCCObject pcc = new PCCObject(texInfo.PCCS[0].Name, GameVersion))
            {
                using (Texture2D tex2D = new Texture2D(pcc, texInfo.PCCS[0].ExpID, GameDirecs))
                {
                    byte[] img = tex2D.ExtractMaxImage(true);
                    using (ImageEngineImage jpg = new ImageEngineImage(img))
                        PreviewImage = jpg.GetWPFBitmap();

                    img = null;
                }
            }
        }

        // This is going to change to pipeline TPL stuff when TPFTools comes along
        public void ChangeTexture(TreeTexInfo tex, string filename)
        {
            Busy = true;
            Status = $"Changing Texture: {tex.TexName}...";
            ProgressIndeterminate = true;

            bool success = ToolsetTextureEngine.ChangeTexture(tex, filename);
            if (success)
            {
                // Add only if not already added.
                if (!ChangedTextures.Contains(tex))
                    ChangedTextures.Add(tex);

                // Re-populate details
                tex.PopulateDetails();

                // Re-generate Thumbnail
                MemoryStream stream = null;
                if (tex.HasChanged)
                    stream = ToolsetTextureEngine.GetThumbFromTex2D(tex.AssociatedTexture);

                using (PCCObject pcc = new PCCObject(tex.PCCS[0].Name, GameVersion))
                    using (Texture2D tex2D = new Texture2D(pcc, tex.PCCS[0].ExpID, GameDirecs))
                        stream = ToolsetTextureEngine.GetThumbFromTex2D(tex2D);

                if (tex.Thumb == null) // Could happen
                    tex.Thumb = new Thumbnail();

                tex.SetChangedThumb(stream);
            }

            ProgressIndeterminate = false;
            Status = $"Texture: {tex.TexName} changed!";
            Busy = false;
        }

        internal void ExtractTexture(TreeTexInfo tex, string filename)
        {
            Busy = true;
            Status = $"Extracting Texture: {tex.TexName}...";

            string error = null;
            try
            {
                ToolsetTextureEngine.ExtractTexture(tex, filename);
            }
            catch (Exception e)
            {
                error = e.Message;
                DebugOutput.PrintLn($"Extracting image {tex.TexName} failed. Reason: {e.ToString()}");
            }


            Busy = false;
            Status = $"Texture: {tex.TexName} " + (error == null ? $"extracted to {filename}!" : $"failed to extract. Reason: {error}.");
        }

        internal void ME1_LowResFix(TreeTexInfo tex)
        {
            Busy = true;
            Status = $"Applying Low Res Fix to {tex.TexName}.";

            string error = null;
            try
            {
                ToolsetTextureEngine.ME1_LowResFix(tex);
            }
            catch(Exception e)
            {
                error = e.Message;
                DebugOutput.PrintLn($"Low Res Fix failed for {tex.TexName}. Reason: {e.ToString()}.");
            }

            Status = error != null ? $"Applied Low Res Fix to {tex.TexName}." : $"Failed to apply Low Res Fix to {tex.TexName}. Reason: {error}.";
            Busy = false;
        }

        void RefreshTreeRelatedProperties()
        {
            // Clear texture folders
            TextureFolders.Clear();
            AllFolders.Clear();
            ChangedTextures.Clear();
            Errors.Clear();
            PreviewImage = null;
            SelectedFolder = null;
            SelectedTexture = null;
            ShowingPreview = false;
            Textures.Clear();  // Just in case

            Properties.Settings.Default.TexplorerGameVersion = GameVersion;
            Properties.Settings.Default.Save();
        }

        internal void DeleteCurrentTree()
        {
            CurrentTree.Delete();
            CurrentTree.Clear(true);

            RefreshTreeRelatedProperties();

            LoadFTSandTree(true);
        }
    }
}
