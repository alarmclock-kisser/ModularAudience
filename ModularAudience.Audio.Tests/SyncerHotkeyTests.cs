using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms;
using ModularAudience.Forms.Hotkeys;
using ModularAudience.Forms.Modules;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class SyncerHotkeyTests
    {
        private const string KeyboardA = "HID#VID_1234&PID_0001#KEYBOARD_A";
        private const string KeyboardB = "HID#VID_1234&PID_0001#KEYBOARD_B";

        [TestMethod]
        public void NumpadSubtractMatchesOnlyTheRegisteredKeyboardUnlessScopeIsDisabled()
        {
            KeyboardInputState input = new();
            HotkeyCombo combo = Scoped(Keys.Subtract);

            Assert.IsFalse(combo.Matches(input.Update(Keys.Subtract, true, KeyboardB)));
            Assert.IsFalse(combo.Matches(input.Update(Keys.OemMinus, true, KeyboardA)));
            Assert.IsTrue(combo.Matches(input.Update(Keys.Subtract, true, KeyboardA.ToLowerInvariant())));

            combo.IsKeyboardSpecific = false;
            Assert.IsTrue(combo.Matches(input.Update(Keys.Subtract, true, KeyboardB)));
            Assert.AreEqual(KeyboardA, combo.KeyboardHardwareId, "Disabling scope must retain the recorded HID.");
            combo.IsKeyboardSpecific = true;
            Assert.IsFalse(combo.Matches(input.Update(Keys.Subtract, true, KeyboardB)));
        }

        [TestMethod]
        public void ScopedModifiersComeFromTheSameKeyboardNotAnotherHeldKeyboard()
        {
            KeyboardInputState input = new();
            HotkeyCombo combo = Scoped(Keys.Subtract, HotkeyModifiers.Shift);
            input.Update(Keys.LShiftKey, true, KeyboardB);
            KeyboardKeyEvent subtract = input.Update(Keys.Subtract, true, KeyboardA);

            Assert.AreEqual(HotkeyModifiers.None, subtract.Modifiers);
            Assert.AreEqual(HotkeyModifiers.Shift, subtract.AllKeyboardModifiers);
            Assert.IsFalse(combo.Matches(subtract));
            combo.IsKeyboardSpecific = false;
            Assert.IsTrue(combo.Matches(subtract), "Universal combinations retain cross-keyboard modifiers.");
            combo.IsKeyboardSpecific = true;
            input.Update(Keys.RShiftKey, true, KeyboardA);
            Assert.IsTrue(combo.Matches(input.Update(Keys.Subtract, true, KeyboardA)));
        }

        [TestMethod]
        public void ReleasingOneShiftDoesNotReleaseTheOtherShiftOrAnotherKeyboard()
        {
            KeyboardInputState input = new();
            input.Update(Keys.LShiftKey, true, KeyboardA);
            input.Update(Keys.RShiftKey, true, KeyboardA);
            input.Update(Keys.LControlKey, true, KeyboardB);

            KeyboardKeyEvent release = input.Update(Keys.LShiftKey, false, KeyboardA);
            Assert.AreEqual(Keys.ShiftKey, release.Key);
            Assert.AreEqual(HotkeyModifiers.Shift, release.Modifiers);
            Assert.AreEqual(HotkeyModifiers.Shift | HotkeyModifiers.Control, release.AllKeyboardModifiers);
            release = input.Update(Keys.RShiftKey, false, KeyboardA);
            Assert.AreEqual(HotkeyModifiers.None, release.Modifiers);
            Assert.AreEqual(HotkeyModifiers.Control, release.AllKeyboardModifiers);
        }

        [TestMethod]
        public void AutoRepeatIsTrackedPerPhysicalKeyboard()
        {
            KeyboardInputState input = new();
            Assert.IsFalse(input.Update(Keys.Subtract, true, KeyboardA).IsRepeat);
            Assert.IsTrue(input.Update(Keys.Subtract, true, KeyboardA).IsRepeat);
            Assert.IsFalse(input.Update(Keys.Subtract, true, KeyboardB).IsRepeat);
            input.Update(Keys.Subtract, false, KeyboardB);
            Assert.IsTrue(input.Update(Keys.Subtract, true, KeyboardA).IsRepeat);
        }

        [TestMethod]
        public void PausingHoldOnlyEndsWhenItsOriginatingPhysicalKeyIsReleased()
        {
            KeyboardInputState input = new();
            SyncerHotkeyState state = new();
            HotkeyCombo pausing = Scoped(Keys.Subtract, HotkeyModifiers.Control);
            HotkeyCombo nudging = new();
            input.Update(Keys.LControlKey, true, KeyboardA);

            Assert.AreEqual(SyncerHotkeyChange.PausingPressed,
                state.Update(input.Update(Keys.Subtract, true, KeyboardA), pausing, nudging, true));
            Assert.AreEqual(SyncerHotkeyChange.None,
                state.Update(input.Update(Keys.Subtract, false, KeyboardB), pausing, nudging, true));
            Assert.AreEqual(SyncerHotkeyChange.None,
                state.Update(input.Update(Keys.OemMinus, false, KeyboardA), pausing, nudging, true));
            Assert.AreEqual(SyncerHotkeyChange.None,
                state.Update(input.Update(Keys.LControlKey, false, KeyboardA), pausing, nudging, true));

            // A release still belongs to the old hold after editing settings or entering a text box.
            pausing.Key = Keys.Add;
            Assert.AreEqual(SyncerHotkeyChange.PausingReleased,
                state.Update(input.Update(Keys.Subtract, false, KeyboardA), pausing, nudging, false));
        }

        [TestMethod]
        public void UniversalPausingHoldWaitsForEveryMatchingKeyboardToRelease()
        {
            KeyboardInputState input = new();
            SyncerHotkeyState state = new();
            HotkeyCombo pausing = Scoped(Keys.Subtract);
            pausing.IsKeyboardSpecific = false;
            HotkeyCombo nudging = new();

            Assert.AreEqual(SyncerHotkeyChange.PausingPressed,
                state.Update(input.Update(Keys.Subtract, true, KeyboardA), pausing, nudging, true));
            Assert.AreEqual(SyncerHotkeyChange.None,
                state.Update(input.Update(Keys.Subtract, true, KeyboardB), pausing, nudging, true));
            Assert.AreEqual(SyncerHotkeyChange.None,
                state.Update(input.Update(Keys.Subtract, false, KeyboardA), pausing, nudging, true));
            Assert.AreEqual(SyncerHotkeyChange.PausingReleased,
                state.Update(input.Update(Keys.Subtract, false, KeyboardB), pausing, nudging, true));
        }

        [TestMethod]
        public void NudgingCapsLockTogglesOnlyOnTheRegisteredKeyboardAndNeverOnRepeatOrRelease()
        {
            KeyboardInputState input = new();
            SyncerHotkeyState state = new();
            HotkeyCombo pausing = new();
            HotkeyCombo nudging = Scoped(Keys.CapsLock);

            Assert.AreEqual(SyncerHotkeyChange.None,
                state.Update(input.Update(Keys.CapsLock, true, KeyboardB), pausing, nudging, true));
            Assert.AreEqual(SyncerHotkeyChange.NudgingPressed,
                state.Update(input.Update(Keys.CapsLock, true, KeyboardA), pausing, nudging, true));
            Assert.AreEqual(SyncerHotkeyChange.None,
                state.Update(input.Update(Keys.CapsLock, true, KeyboardA), pausing, nudging, true));
            Assert.AreEqual(SyncerHotkeyChange.None,
                state.Update(input.Update(Keys.CapsLock, false, KeyboardA), pausing, nudging, true));
            Assert.AreEqual(SyncerHotkeyChange.None,
                state.Update(input.Update(Keys.CapsLock, true, KeyboardA), pausing, nudging, false));
        }

        [TestMethod]
        public void SingleModifierHotkeyIgnoresItsOwnInherentModifier()
        {
            KeyboardInputState input = new();
            HotkeyCombo pausing = Scoped(Keys.ShiftKey);
            Assert.IsTrue(pausing.Matches(input.Update(Keys.RShiftKey, true, KeyboardA)));
            Assert.IsFalse(pausing.Matches(input.Update(Keys.LShiftKey, true, KeyboardB)));
        }

        [TestMethod]
        public void PausingWithBothShiftKeysHeldWaitsForBothPhysicalKeysToRelease()
        {
            KeyboardInputState input = new();
            SyncerHotkeyState state = new();
            HotkeyCombo pausing = Scoped(Keys.ShiftKey);
            HotkeyCombo nudging = new();
            Assert.AreEqual(SyncerHotkeyChange.PausingPressed,
                state.Update(input.Update(Keys.LShiftKey, true, KeyboardA), pausing, nudging, true));
            Assert.AreEqual(SyncerHotkeyChange.None,
                state.Update(input.Update(Keys.RShiftKey, true, KeyboardA), pausing, nudging, true));
            Assert.AreEqual(SyncerHotkeyChange.None,
                state.Update(input.Update(Keys.LShiftKey, false, KeyboardA), pausing, nudging, true));
            Assert.AreEqual(SyncerHotkeyChange.PausingReleased,
                state.Update(input.Update(Keys.RShiftKey, false, KeyboardA), pausing, nudging, true));
        }

        [TestMethod]
        public void SyncerHardwareIdentityAndBothCheckboxStatesSurviveJsonRoundTrip()
        {
            WithSettingsPath(path =>
            {
                HotkeySettingsManager settings = new(path)
                {
                    PausingSyncerHotkey = Scoped(Keys.Subtract, HotkeyModifiers.Control),
                    NudgingSyncerHotkey = new HotkeyCombo
                    {
                        Key = Keys.Add,
                        KeyboardHardwareId = KeyboardB,
                        KeyboardDisplayName = "Second keyboard",
                        IsKeyboardSpecific = false
                    }
                };

                HotkeySettingsManager restored = new(path);
                Assert.AreEqual(Keys.Subtract, restored.PausingSyncerHotkey.Key);
                Assert.AreEqual(HotkeyModifiers.Control, restored.PausingSyncerHotkey.Modifiers);
                Assert.AreEqual(KeyboardA, restored.PausingSyncerHotkey.KeyboardHardwareId);
                Assert.IsTrue(restored.PausingSyncerHotkey.IsKeyboardSpecific);
                Assert.AreEqual(KeyboardB, restored.NudgingSyncerHotkey.KeyboardHardwareId);
                Assert.AreEqual("Second keyboard", restored.NudgingSyncerHotkey.KeyboardDisplayName);
                Assert.IsFalse(restored.NudgingSyncerHotkey.IsKeyboardSpecific);
                Assert.AreEqual(2, restored.GetProfiles().Count, "Disconnected syncer keyboards must remain identifiable.");

                HotkeyCombo pausing = restored.PausingSyncerHotkey;
                HotkeyCombo nudging = restored.NudgingSyncerHotkey;
                pausing.IsKeyboardSpecific = false;
                nudging.IsKeyboardSpecific = true;
                restored.PausingSyncerHotkey = pausing;
                restored.NudgingSyncerHotkey = nudging;
                restored = new(path);
                Assert.IsFalse(restored.PausingSyncerHotkey.IsKeyboardSpecific);
                Assert.IsTrue(restored.NudgingSyncerHotkey.IsKeyboardSpecific);
                Assert.AreEqual(KeyboardA, restored.PausingSyncerHotkey.KeyboardHardwareId);
                using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
                Assert.AreEqual(4, json.RootElement.GetProperty("Version").GetInt32());
            });
        }

        [TestMethod]
        public void LegacySyncerSettingsWithoutAnHidStayUniversalUntilRegisteredAgain()
        {
            WithSettingsPath(path =>
            {
                File.WriteAllText(path, """
                    { "Version": 3, "Bindings": [],
                      "PausingSyncerHotkey": { "Key": "ShiftKey", "Modifiers": "None" },
                      "NudgingSyncerHotkey": { "Key": "CapsLock", "Modifiers": "None" } }
                    """);
                HotkeySettingsManager settings = new(path);
                Assert.IsFalse(settings.PausingSyncerHotkey.IsKeyboardSpecific);
                Assert.IsFalse(settings.NudgingSyncerHotkey.IsKeyboardSpecific);
                Assert.AreEqual(Keys.ShiftKey, settings.PausingSyncerHotkey.Key);
                Assert.AreEqual(Keys.CapsLock, settings.NudgingSyncerHotkey.Key);
                Assert.IsTrue(settings.PausingSyncerHotkey.Matches(
                    new KeyboardInputState().Update(Keys.LShiftKey, true, KeyboardB)));
            });
        }

        [TestMethod]
        public void FirstRegistrationScopesTheActualKeyboardAndReregistrationRetainsUniversalChoice()
        {
            HotkeyCombo legacy = new() { Key = Keys.ShiftKey, IsKeyboardSpecific = false };
            HotkeyCombo captured = HotkeyManager.CreateSyncerHotkey(
                legacy, Keys.Subtract, HotkeyModifiers.None, KeyboardA, "Physical keyboard A");
            Assert.AreEqual(KeyboardA, captured.KeyboardHardwareId);
            Assert.AreEqual("Physical keyboard A", captured.KeyboardDisplayName);
            Assert.IsTrue(captured.IsKeyboardSpecific, "The first physical registration must default to keyboard-specific.");

            captured.IsKeyboardSpecific = false;
            HotkeyCombo recaptured = HotkeyManager.CreateSyncerHotkey(
                captured, Keys.Add, HotkeyModifiers.Shift, KeyboardB, "Physical keyboard B");
            Assert.AreEqual(KeyboardB, recaptured.KeyboardHardwareId);
            Assert.AreEqual(Keys.Add, recaptured.Key);
            Assert.IsFalse(recaptured.IsKeyboardSpecific, "An explicitly unchecked scope must survive changing the hotkey.");
        }

        [TestMethod]
        public void LegacyTrackAndCollectionTargetsMigrateWithoutKeepingNamedOwners()
        {
            WithSettingsPath(path =>
            {
                HotkeySettingsFile legacy = new()
                {
                    Version = 3,
                    Bindings =
                    [
                        new HotkeyBinding
                        {
                            Key = Keys.F1,
                            ActionName = "Track View #00 - Loop",
                            Target = new()
                            {
                                OwnerType = typeof(TrackView).FullName!, OwnerTitle = "#00 - Old track",
                                TrackViewId = 0, UseLastFocusedTrackView = true
                            }
                        },
                        new HotkeyBinding
                        {
                            Key = Keys.F2,
                            ActionName = "Old collection - Time stretch selected",
                            Target = new()
                            {
                                OwnerType = typeof(AudioCollectionView).FullName!, OwnerTitle = "Old collection",
                                ActionKind = HotkeyActionKinds.ContextMenuItem,
                                MenuItemPath = [new() { Name = "menuToolStripItem_timeStretchSelected", Text = "Time stretch selected" }]
                            }
                        }
                    ]
                };
                File.WriteAllText(path, JsonSerializer.Serialize(legacy,
                    new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }));
                HotkeySettingsManager settings = new(path);
                HotkeyBinding track = settings.Snapshot().Single(binding => binding.Key == Keys.F1);
                Assert.IsFalse(track.Target.UseLastFocusedTrackView);
                Assert.AreEqual(string.Empty, track.Target.OwnerTitle);
                Assert.AreEqual(0, track.Target.TrackViewId);
                HotkeyBinding collection = settings.Snapshot().Single(binding => binding.Key == Keys.F2);
                Assert.IsTrue(collection.Target.UseCollectionSelection);
                Assert.AreEqual(string.Empty, collection.Target.OwnerTitle);
                Assert.AreEqual("AudioCollectionView(s) Selection -> Time stretch selected", collection.ActionName);
            });
        }

        [TestMethod]
        public void CopyingKeyboardProfilePreservesCollectionPositionAndGlobalSelectionScope()
        {
            WithSettingsPath(path =>
            {
                HotkeySettingsManager settings = new(path);
                settings.AddOrReplace(new()
                {
                    Key = Keys.F1, KeyboardHardwareId = KeyboardA,
                    Target = new()
                    {
                        OwnerType = typeof(AudioCollectionView).FullName!, CollectionItemIndex = 0,
                        Gesture = HotkeyMouseGesture.LeftDoubleClick
                    }
                });
                settings.AddOrReplace(new()
                {
                    Key = Keys.F2, KeyboardHardwareId = KeyboardA,
                    Target = new()
                    {
                        OwnerType = typeof(AudioCollectionView).FullName!,
                        ActionKind = HotkeyActionKinds.ContextMenuItem, UseCollectionSelection = true,
                        MenuItemPath = [new() { Name = "menuToolStripItem_timeStretchSelected", Text = "Time stretch selected" }]
                    }
                });
                Assert.AreEqual(2, settings.CopyProfile(KeyboardA, "A", KeyboardB, "B"));
                HotkeySettingsManager restored = new(path);
                IReadOnlyList<HotkeyBinding> copies = restored.GetBindingsForScope(KeyboardB);
                Assert.AreEqual(0, copies.Single(binding => binding.Key == Keys.F1).Target.CollectionItemIndex);
                Assert.IsTrue(copies.Single(binding => binding.Key == Keys.F2).Target.UseCollectionSelection);
            });
        }

        private static HotkeyCombo Scoped(Keys key, HotkeyModifiers modifiers = HotkeyModifiers.None) => new()
        {
            Key = key, Modifiers = modifiers, KeyboardHardwareId = KeyboardA,
            KeyboardDisplayName = "First keyboard", IsKeyboardSpecific = true
        };

        private static void WithSettingsPath(Action<string> verify)
        {
            string directory = Path.Combine(Path.GetTempPath(), "ModularAudience.HotkeyTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                verify(Path.Combine(directory, "hotkeys.json"));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
