using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processors_V4;
using ModularAudience.Forms;
using System.Collections;
using System.Reflection;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class DeterministicSeparationDialogTests
    {
        [STATestMethod]
        public void ConstructionAndDisposalInitializeDesignerControlsWithoutStartingAnalysis()
        {
            AssertIsolatedSta();
            using AudioTestScope scope = new();
            AudioObj source = scope.Create([0.25f, -0.125f], 8000);
            using DeterministicSeparationDialog dialog = new(source);
            dialog.PerformLayout();
            Assert.IsFalse(dialog.Visible, "Constructing the dialog must not show it automatically.");
            AssertIdleWithoutAnalysis(dialog);
            Assert.AreEqual((decimal) Environment.ProcessorCount, GetControl<NumericUpDown>(dialog, "numeric_threads").Maximum,
                "The thread selector maximum must match the logical core count.");
            Assert.IsInstanceOfType(GetControl<ComboBox>(dialog, "comboBox_windowSize").SelectedItem, typeof(int),
                "FFT options must remain boxed integers, including during designer layout.");
            DeterministicSeparationSettings settings = (DeterministicSeparationSettings) Invoke(dialog, "ReadSettings")!;
            settings.Validate();
            Assert.AreEqual(4096, settings.WindowSize, "The default FFT selection must be readable without an invalid cast.");
            Button separate = GetControl<Button>(dialog, "button_separate");

            dialog.Dispose();

            Assert.IsTrue(dialog.IsDisposed, "Disposing an idle dialog must release the form.");
            Assert.IsTrue(separate.IsDisposed, "Designer-created controls must be disposed with the dialog.");
            Assert.IsNull(GetField(dialog, "analysis").GetValue(dialog), "Disposal must release the cached analysis.");
            Assert.IsNull(GetField(dialog, "operationCancellation").GetValue(dialog), "An idle dialog must not leave an operation alive.");
            CollectionAssert.AreEqual(new[] { 0.25f, -0.125f }, source.Data, "The dialog must not dispose or modify its borrowed source.");
            Assert.IsNull(WindowMain.Instance, "Dialog construction must not start the main application.");
        }

        [STATestMethod]
        [DataRow("comboBox_windowSize")]
        [DataRow("numeric_iterations")]
        [DataRow("numeric_medianFrames")]
        public void ChangingASettingInvalidatesCachedAnalysisAndDisablesSeparation(string controlName)
        {
            AssertIsolatedSta();
            using AudioTestScope scope = new();
            AudioObj source = scope.Create(AudioTestData.Tone(8000, 80, 500, 5, 5), 8000);
            using DeterministicSeparationDialog dialog = new(source);
            ConfigureSmallAnalysis(dialog);
            DeterministicSeparationSettings settings = (DeterministicSeparationSettings) Invoke(dialog, "ReadSettings")!;
            DeterministicSeparationAnalysis analysis = DeterministicSeparationProcessor.AnalyzeAsync(source, settings).GetAwaiter().GetResult();
            Assert.IsTrue(analysis.Sources.Count > 0, "The invalidation regression requires a populated analysis.");
            GetField(dialog, "analysis").SetValue(dialog, analysis);
            Invoke(dialog, "DisplayAnalysis", analysis);
            Invoke(dialog, "SetOperationState", false);
            Assert.IsTrue(GetControl<Button>(dialog, "button_separate").Enabled, "The seeded current analysis must enable separation.");
            Assert.AreEqual(analysis.Sources.Count, GetControl<DataGridView>(dialog, "dataGridView_sources").Rows.Count);

            if (controlName == "comboBox_windowSize")
                GetControl<ComboBox>(dialog, controlName).SelectedItem = 512;
            else
                GetControl<NumericUpDown>(dialog, controlName).Value += 2;

            AssertIdleWithoutAnalysis(dialog);
            Assert.AreEqual("Settings changed. Detect sources again before separating.", GetControl<Label>(dialog, "label_status").Text,
                "Changing settings must clearly invalidate the cached model.");
            if (controlName == "comboBox_windowSize")
                Assert.AreEqual("128 samples (fixed)", GetControl<Label>(dialog, "label_hopSizeValue").Text,
                    "The FFT selection event must safely update the fixed hop size.");
        }

        [STATestMethod]
        public void AdvancedControlsDefaultToOffAndAutomaticWithNoProfiles()
        {
            AssertIsolatedSta();
            using AudioTestScope scope = new();
            AudioObj source = scope.Create([0.25f, -0.125f], 8000);
            using DeterministicSeparationDialog dialog = new(source);
            dialog.PerformLayout();

            Assert.IsFalse(GetControl<CheckBox>(dialog, "checkBox_cqtAnalysis").Checked, "CQT analysis must default off.");
            Assert.IsFalse(GetControl<CheckBox>(dialog, "checkBox_cqtSynthesis").Checked, "CQT synthesis must default off.");
            Assert.IsFalse(GetControl<CheckBox>(dialog, "checkBox_pyin").Checked, "pYIN must default off.");
            Assert.IsFalse(GetControl<CheckBox>(dialog, "checkBox_ilrma").Checked, "ILRMA must default off.");
            // All four advanced DSP cores (C1, C2, D, E) are now wired.
            Assert.IsTrue(GetControl<CheckBox>(dialog, "checkBox_cqtAnalysis").Enabled, "CQT analysis must be enabled.");
            Assert.IsTrue(GetControl<CheckBox>(dialog, "checkBox_pyin").Enabled, "pYIN must be enabled.");
            Assert.IsTrue(GetControl<CheckBox>(dialog, "checkBox_cqtSynthesis").Enabled, "CQT synthesis must be enabled.");
            Assert.IsTrue(GetControl<CheckBox>(dialog, "checkBox_ilrma").Enabled, "ILRMA must be enabled.");

            DomainUpDown ensemble = GetControl<DomainUpDown>(dialog, "domainUpDown_ensembleMode");
            Assert.AreEqual("Automatic", ensemble.Text, "The ensemble mode must default to Automatic.");
            CollectionAssert.AreEqual(new object[] { "Automatic", "ProfilesOnly", "ProfilesAndAutomatic" }, ensemble.Items.Cast<object>().ToArray(),
                "The ensemble selector must expose exactly the three supported modes.");

            CheckedListBox profiles = GetControl<CheckedListBox>(dialog, "checkedListBox_profiles");
            Assert.AreEqual(16, profiles.Items.Count, "All 16 catalog instrument profiles must be listed for selection.");
            Assert.AreEqual(0, profiles.CheckedItems.Count, "No profile may be preselected by default.");

            DeterministicSeparationSettings settings = (DeterministicSeparationSettings) Invoke(dialog, "ReadSettings")!;
            settings.Validate();
            Assert.IsFalse(settings.UseCqtAnalysis && settings.UseCqtSynthesis && settings.UsePyin && settings.UseIlrma);
            Assert.AreEqual(InstrumentEnsembleMode.Automatic, settings.EnsembleMode);
            Assert.IsTrue(settings.InstrumentProfiles.IsEmpty, "Automatic mode must not carry instrument profiles.");
        }

        [STATestMethod]
        public void ChangingEnsembleModeOrProfileSelectionInvalidatesAnalysis()
        {
            AssertIsolatedSta();
            using AudioTestScope scope = new();
            AudioObj source = scope.Create(AudioTestData.Tone(8000, 80, 500, 5, 5), 8000);
            using DeterministicSeparationDialog dialog = new(source);
            ConfigureSmallAnalysis(dialog);

            GetControl<DomainUpDown>(dialog, "domainUpDown_ensembleMode").Text = "ProfilesOnly";
            CheckedListBox profiles = GetControl<CheckedListBox>(dialog, "checkedListBox_profiles");
            profiles.SetItemChecked(0, true);
            DeterministicSeparationSettings guided = (DeterministicSeparationSettings) Invoke(dialog, "ReadSettings")!;
            Assert.AreEqual(InstrumentEnsembleMode.ProfilesOnly, guided.EnsembleMode);
            Assert.AreEqual(1, guided.InstrumentProfiles.Length, "A checked profile must be readable as an immutable selection.");

            // Seed the cached analysis from the default Automatic settings (the only connected pipeline path).
            GetControl<DomainUpDown>(dialog, "domainUpDown_ensembleMode").Text = "Automatic";
            profiles.SetItemChecked(0, false);
            DeterministicSeparationSettings baseline = (DeterministicSeparationSettings) Invoke(dialog, "ReadSettings")!;
            baseline.Validate();
            DeterministicSeparationAnalysis analysis = DeterministicSeparationProcessor.AnalyzeAsync(source, baseline).GetAwaiter().GetResult();
            GetField(dialog, "analysis").SetValue(dialog, analysis);
            Invoke(dialog, "DisplayAnalysis", analysis);
            Invoke(dialog, "SetOperationState", false);
            Assert.IsTrue(GetControl<Button>(dialog, "button_separate").Enabled, "The seeded current analysis must enable separation.");

            profiles.SetItemChecked(1, true);
            AssertIdleWithoutAnalysis(dialog);
            Assert.AreEqual("Instrument profile selection changed. Detect sources again before separating.", GetControl<Label>(dialog, "label_status").Text,
                "Changing the committed profile selection must invalidate the cached model.");

            GetControl<DomainUpDown>(dialog, "domainUpDown_ensembleMode").Text = "ProfilesAndAutomatic";
            AssertIdleWithoutAnalysis(dialog);
            Assert.AreEqual("Settings changed. Detect sources again before separating.", GetControl<Label>(dialog, "label_status").Text,
                "Changing the ensemble mode must invalidate the cached model.");
        }

        [STATestMethod]
        public void ReadSettingsRoundTripsEnsembleAndProfilesThroughIsEquivalentTo()
        {
            AssertIsolatedSta();
            using AudioTestScope scope = new();
            AudioObj source = scope.Create([0.25f, -0.125f], 8000);
            using DeterministicSeparationDialog dialog = new(source);

            GetControl<DomainUpDown>(dialog, "domainUpDown_ensembleMode").Text = "ProfilesOnly";
            CheckedListBox profiles = GetControl<CheckedListBox>(dialog, "checkedListBox_profiles");
            profiles.SetItemChecked(2, true);
            profiles.SetItemChecked(0, true);

            DeterministicSeparationSettings first = (DeterministicSeparationSettings) Invoke(dialog, "ReadSettings")!;
            DeterministicSeparationSettings second = (DeterministicSeparationSettings) Invoke(dialog, "ReadSettings")!;
            Assert.IsTrue(first.IsEquivalentTo(second), "Repeated reads of the same UI state must be semantically equivalent.");

            profiles.SetItemChecked(0, false);
            DeterministicSeparationSettings third = (DeterministicSeparationSettings) Invoke(dialog, "ReadSettings")!;
            Assert.IsFalse(first.IsEquivalentTo(third), "Deselecting a profile must change the semantic settings.");
            Assert.AreEqual(InstrumentEnsembleMode.ProfilesOnly, second.EnsembleMode);
            Assert.AreEqual(2, second.InstrumentProfiles.Length);
        }

        [STATestMethod]
        public void ThirdSeparationMenuItemOpensAnIdleModelessDialogOnActualClick()
        {
            AssertIsolatedSta();
            using AudioTestScope scope = new();
            AudioObj source = scope.Create([0.25f, -0.125f, 0.0625f], 8000);
            IList registry = GetCollectionRegistry();
            object[] previousCollections = registry.Cast<object>().ToArray();
            Form[] previousForms = Application.OpenForms.Cast<Form>().ToArray();
            using AudioCollectionView collection = new([source], "Deterministic separator regression");
            try
            {
                Assert.IsFalse(GetControl<CheckBox>(collection, "checkBox_autoPlay").Checked,
                    "The menu regression must never enable audio playback.");
                Assert.IsFalse(GetControl<CheckBox>(collection, "checkBox_preview").Checked,
                    "The menu regression must not start waveform preview work.");
                GetControl<ListBox>(collection, "listBox_audios").SelectedItem = source;
                Invoke(collection, "UpdateContextMenuState");
                ToolStripMenuItem menu = GetFieldValue<ToolStripMenuItem>(collection, "menuToolStripItem_demucsSeparateSelected");
                Assert.AreEqual(3, menu.DropDownItems.Count, "Source Separation must expose all three separation modes.");
                ToolStripItem deterministic = menu.DropDownItems[2];
                Assert.AreEqual("Best-practice deterministic", deterministic.Text, "The third menu label must identify deterministic separation.");
                Assert.AreSame(GetFieldValue<ToolStripMenuItem>(collection, "menuToolStripItem_sourceSeparateDeterministic"), deterministic,
                    "The third item must be the designer-wired deterministic action.");
                Assert.IsTrue(deterministic.Enabled, "Selecting exactly one source must enable deterministic separation.");
                VerifyModelessClick(collection, deterministic, previousForms);
            }
            finally
            {
                CloseNewDialogs(previousForms);
                CloseCollection(collection, registry);
            }
            CollectionAssert.AreEqual(previousCollections, registry.Cast<object>().ToArray(),
                "The menu regression must restore the collection registry.");
            CollectionAssert.AreEquivalent(previousForms, Application.OpenForms.Cast<Form>().ToArray(),
                "The menu regression must not leave any windows open.");
            Assert.IsNull(WindowMain.Instance, "Clicking the separator must not construct the main application.");
        }

        private static void AssertIsolatedSta()
        {
            Assert.AreEqual(ApartmentState.STA, Thread.CurrentThread.GetApartmentState(), "WinForms tests require an STA thread.");
            Assert.IsNull(WindowMain.Instance, "These tests must run without starting the main application.");
        }

        private static void AssertIdleWithoutAnalysis(DeterministicSeparationDialog dialog)
        {
            Assert.IsNull(GetField(dialog, "analysis").GetValue(dialog), "No source analysis may run implicitly.");
            Assert.IsNull(GetField(dialog, "operationCancellation").GetValue(dialog), "No background operation may start implicitly.");
            Assert.IsFalse(GetControl<Button>(dialog, "button_separate").Enabled, "Separate must be disabled without a current analysis.");
            Assert.IsTrue(GetControl<Button>(dialog, "button_detect").Enabled, "Source detection must remain available while idle.");
            Assert.IsFalse(GetControl<Button>(dialog, "button_cancel").Enabled, "Cancel must be disabled while idle.");
            DataGridView grid = GetControl<DataGridView>(dialog, "dataGridView_sources");
            Assert.AreEqual(0, grid.Rows.Count, "An invalidated or new dialog must not retain source rows.");
            Assert.IsFalse(grid.Enabled, "Source selection must be disabled without a current analysis.");
            Assert.AreEqual(0, GetControl<ProgressBar>(dialog, "progressBar_operation").Value, "Idle progress must be reset.");
        }

        private static void ConfigureSmallAnalysis(DeterministicSeparationDialog dialog)
        {
            GetControl<ComboBox>(dialog, "comboBox_windowSize").SelectedItem = 256;
            GetControl<NumericUpDown>(dialog, "numeric_maxComponents").Value = 4;
            GetControl<NumericUpDown>(dialog, "numeric_iterations").Value = 4;
            GetControl<NumericUpDown>(dialog, "numeric_analysisFrames").Value = 32;
            GetControl<NumericUpDown>(dialog, "numeric_blockFrames").Value = 8;
            GetControl<NumericUpDown>(dialog, "numeric_medianFrames").Value = 5;
            GetControl<NumericUpDown>(dialog, "numeric_medianBins").Value = 5;
            GetControl<NumericUpDown>(dialog, "numeric_threads").Value = 1;
        }

        private static void VerifyModelessClick(AudioCollectionView owner, ToolStripItem item, Form[] previousForms)
        {
            bool modalObserved = false;
            EventHandler closeUnexpectedModal = (_, _) =>
            {
                foreach (DeterministicSeparationDialog dialog in NewDialogs(previousForms).Where(dialog => dialog.Modal))
                {
                    modalObserved = true;
                    dialog.Close();
                    dialog.Dispose();
                }
            };
            Application.Idle += closeUnexpectedModal;
            try
            {
                item.PerformClick();
                Assert.IsFalse(modalObserved, "The deterministic menu action must not block in ShowDialog.");
                DeterministicSeparationDialog[] opened = NewDialogs(previousForms);
                Assert.AreEqual(1, opened.Length, "The actual menu Click event must open exactly one deterministic dialog.");
                Assert.IsTrue(opened[0].Visible && !opened[0].Modal, "The separator must open visibly and modelessly.");
                Assert.AreSame(owner, opened[0].Owner, "The modeless dialog must retain its collection owner.");
                AssertIdleWithoutAnalysis(opened[0]);
            }
            finally
            {
                Application.Idle -= closeUnexpectedModal;
            }
        }

        private static DeterministicSeparationDialog[] NewDialogs(Form[] previousForms)
            => Application.OpenForms.OfType<DeterministicSeparationDialog>().Where(dialog => !previousForms.Contains(dialog)).ToArray();

        private static void CloseNewDialogs(Form[] previousForms)
        {
            foreach (DeterministicSeparationDialog dialog in NewDialogs(previousForms))
            {
                dialog.Close();
                dialog.Dispose();
            }
        }

        private static void CloseCollection(AudioCollectionView collection, IList registry)
        {
            AudioCollection audioCollection = GetFieldValue<AudioCollection>(collection, "AudioC");
            using System.Windows.Forms.Timer preview = GetFieldValue<System.Windows.Forms.Timer>(collection, "waveformPreviewTimer");
            using SemaphoreSlim autoPlayLock = GetFieldValue<SemaphoreSlim>(collection, "autoPlayLock");
            try
            {
                GetControl<ListBox>(collection, "listBox_audios").DataSource = null;
                audioCollection.Audios.Clear();
                collection.Close();
            }
            finally
            {
                preview.Stop();
                collection.Dispose();
                audioCollection.Dispose();
                registry.Remove(collection);
            }
        }

        private static IList GetCollectionRegistry()
        {
            FieldInfo? field = typeof(WindowMain).GetField("CollectionViews", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "The collection registry must be available for isolated form cleanup.");
            return (IList) field.GetValue(null)!;
        }

        private static T GetControl<T>(Control parent, string name) where T : Control
        {
            Control[] matches = parent.Controls.Find(name, true);
            Assert.AreEqual(1, matches.Length, $"Expected exactly one designer control named {name}.");
            Assert.IsInstanceOfType(matches[0], typeof(T), $"Designer control {name} has an unexpected type.");
            Assert.AreSame(GetField(parent, name).GetValue(parent), matches[0], $"Designer field {name} must reference the displayed control.");
            return (T) matches[0];
        }

        private static T GetFieldValue<T>(object instance, string name)
        {
            object? value = GetField(instance, name).GetValue(instance);
            Assert.IsInstanceOfType(value, typeof(T), $"Field {name} has an unexpected value or type.");
            return (T) value!;
        }

        private static FieldInfo GetField(object instance, string name)
        {
            FieldInfo? field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected instance field {name} on {instance.GetType().Name}.");
            return field;
        }

        private static object? Invoke(object instance, string name, params object[] arguments)
        {
            MethodInfo? method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, $"Expected instance method {name} on {instance.GetType().Name}.");
            return method.Invoke(instance, arguments);
        }
    }
}
