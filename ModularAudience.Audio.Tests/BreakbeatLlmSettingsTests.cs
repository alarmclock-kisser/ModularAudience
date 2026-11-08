using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class BreakbeatLlmSettingsTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Field(object target, string name) => target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);

    [TestMethod]
    public void PersistenceRetainsValuesAndIndependentSendFlags()
    {
        string directory = Path.Combine(Path.GetTempPath(), "breakbeat-llm-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        try
        {
            var settings = BreakbeatLlmSettings.Load(path);
            settings.Address = "http://localhost:9000/v1";
            settings.Prompt = "Vary the previous beat";
            settings.KeepContext = true;
            settings.JsonResponse = false;
            settings.Parameters["seed"].Value = 123;
            settings.Parameters["seed"].Enabled = false;
            settings.Parameters["top_k"].Value = 23;
            settings.Parameters["top_k"].Enabled = true;
            settings.Save(path);
            var loaded = BreakbeatLlmSettings.Load(path);
            Assert.AreEqual(settings.Address, loaded.Address);
            Assert.AreEqual(settings.Prompt, loaded.Prompt);
            Assert.IsTrue(loaded.KeepContext);
            Assert.IsFalse(loaded.JsonResponse);
            Assert.AreEqual(123d, loaded.Parameters["seed"].Value);
            Assert.IsFalse(loaded.Parameters["seed"].Enabled);
            Assert.AreEqual(23d, loaded.Parameters["top_k"].Value);
            Assert.IsTrue(loaded.Parameters["top_k"].Enabled);
            Assert.IsFalse(File.ReadAllText(path).Contains("assistant"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void PayloadContainsOnlyEnabledParametersWithCorrectJsonTypes()
    {
        var settings = new BreakbeatLlmSettings(); settings.Normalize();
        settings.Parameters["top_k"].Enabled = true;
        Dictionary<string, object?> payload = new() { ["seed"] = 999, ["model"] = "test" };
        settings.ApplyTo(payload);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        Assert.AreEqual(0.6, json.RootElement.GetProperty("temperature").GetDouble());
        Assert.AreEqual(40, json.RootElement.GetProperty("top_k").GetInt64());
        Assert.IsFalse(json.RootElement.TryGetProperty("seed", out _));
        Assert.IsFalse(json.RootElement.TryGetProperty("min_p", out _));
        Assert.AreEqual("json_object", json.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        settings.JsonResponse = false; settings.Parameters["temperature"].Enabled = false;
        settings.ApplyTo(payload);
        Assert.IsFalse(payload.ContainsKey("temperature"));
        Assert.IsFalse(payload.ContainsKey("response_format"));
        Assert.AreEqual("test", payload["model"]);
    }

    [TestMethod]
    public void ValidationRejectsNonFiniteOutOfRangeAndFractionalIntegers()
    {
        var temperature = BreakbeatLlmSettings.Definitions.Single(d => d.Key == "temperature");
        Assert.AreEqual(0.75, temperature.Parse("0,75"));
        foreach (string value in new[] { "NaN", "Infinity", "-0.1", "2.01", "text" })
            Assert.ThrowsExactly<ArgumentException>(() => temperature.Parse(value));
        var tokens = BreakbeatLlmSettings.Definitions.Single(d => d.Key == "max_tokens");
        Assert.ThrowsExactly<ArgumentException>(() => tokens.Parse("1.5"));
        Assert.ThrowsExactly<ArgumentException>(() => tokens.Parse("0"));
    }

    [TestMethod]
    public void ContextPreservesCompleteTurnsAndFreshSystemPromptOnlyWithinItsInstance()
    {
        var first = new BreakbeatLlmContext(); var second = new BreakbeatLlmContext();
        first.Messages("endpoint|model", "old system", "first request", true);
        first.Remember(first.Generation, "first request", "first reply");
        var messages = first.Messages("endpoint|model", "new system", "variation", true);
        CollectionAssert.AreEqual(new[] { "system", "user", "assistant", "user" }, messages.Select(m => m.role).ToArray());
        CollectionAssert.AreEqual(new[] { "new system", "first request", "first reply", "variation" }, messages.Select(m => m.content).ToArray());
        Assert.HasCount(2, second.Messages("endpoint|model", "system", "request", true));
        Assert.HasCount(2, first.Messages("endpoint|model", "system", "request", false));
        Assert.HasCount(2, first.Messages("endpoint|different model", "system", "request", true));
        Assert.AreEqual(0, first.TurnCount);
    }

    [TestMethod]
    public void ClearingContextRejectsLateResponsesAndHistoryIsBounded()
    {
        var context = new BreakbeatLlmContext();
        context.Messages("endpoint|model", "system", "request", true);
        int generation = context.Generation;
        context.Clear();
        context.Messages("endpoint|model", "system", "new request", true);
        context.Remember(generation, "request", "late response");
        Assert.AreEqual(0, context.TurnCount);
        for (int i = 0; i < 10; i++) context.Remember(context.Generation, $"user {i}", $"reply {i}");
        Assert.AreEqual(BreakbeatLlmContext.MaximumTurns, context.TurnCount);
        Assert.AreEqual("user 2", context.Messages("endpoint|model", "system", "next", true)[1].content);
        context.Remember(context.Generation, "large", new string('a', BreakbeatLlmContext.MaximumCharacters - 10));
        Assert.AreEqual(1, context.TurnCount);
    }

    [STATestMethod]
    public void OpenWindowsShareSettingsButNeverConversationAndUncheckingClearsEveryWindow()
    {
        string path = BreakbeatLlmSettings.SettingsPath;
        byte[]? original = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var sharedField = typeof(BreakbeatGeneratorDialog).GetField("sharedLlmSettings", BindingFlags.NonPublic | BindingFlags.Static)!;
        object? originalShared = sharedField.GetValue(null);
        var initial = new BreakbeatLlmSettings(); initial.Normalize(); sharedField.SetValue(null, initial);
        try
        {
            using var first = new BreakbeatGeneratorDialog([]);
            using var second = new BreakbeatGeneratorDialog([]);
            var firstPrompt = (TextBox)Field(first, "textBox_prompt");
            firstPrompt.Text = "Sync before the save timer fires";
            Assert.AreEqual(firstPrompt.Text, ((TextBox)Field(second, "textBox_prompt")).Text);
            using var third = new BreakbeatGeneratorDialog([]);
            Assert.AreEqual(firstPrompt.Text, ((TextBox)Field(third, "textBox_prompt")).Text);

            var editors = (Dictionary<string, (ToolStripMenuItem Item, ToolStripTextBox Editor)>)Field(first, "llmParameterEditors");
            editors["seed"].Editor.Text = "321";
            editors["seed"].Item.PerformClick();
            editors["seed"].Item.HideDropDown();
            Assert.IsTrue(editors["seed"].Item.Checked, "One click must toggle exactly once.");
            Invoke(first, "CaptureLlmSettings");
            var secondSettings = (BreakbeatLlmSettings)Field(second, "llmSettings");
            Assert.IsTrue(secondSettings.Parameters["seed"].Enabled);
            Assert.AreEqual(321d, secondSettings.Parameters["seed"].Value);

            ((ToolStripMenuItem)Field(first, "keepContextItem")).Checked = true;
            var firstContext = (BreakbeatLlmContext)Field(first, "llmContext");
            var secondContext = (BreakbeatLlmContext)Field(second, "llmContext");
            firstContext.Messages("endpoint|model", "system", "first", true);
            firstContext.Remember(firstContext.Generation, "first", "reply");
            Assert.AreEqual(0, secondContext.TurnCount);
            secondContext.Messages("endpoint|model", "system", "second", true);
            int generation = secondContext.Generation;
            secondContext.Remember(generation, "second", "reply");
            ((ToolStripMenuItem)Field(first, "keepContextItem")).Checked = false;
            Assert.AreEqual(0, firstContext.TurnCount);
            Assert.AreEqual(0, secondContext.TurnCount);
            ((ToolStripMenuItem)Field(first, "keepContextItem")).Checked = true;
            secondContext.Remember(generation, "second", "late reply");
            Assert.AreEqual(0, secondContext.TurnCount);

            ((ToolStripMenuItem)((Button)Field(first, "button_llm")).ContextMenuStrip!.Items[^1]).PerformClick();
            Assert.IsFalse(secondSettings.Parameters["seed"].Enabled);
            Assert.AreEqual(42d, secondSettings.Parameters["seed"].Value);
            Assert.AreEqual(42d, BreakbeatLlmSettings.Load().Parameters["seed"].Value);
            firstContext.Messages("endpoint|model", "system", "last", true);
            firstContext.Remember(firstContext.Generation, "last", "reply");
            first.Dispose();
            Assert.AreEqual(0, firstContext.TurnCount);
        }
        finally
        {
            sharedField.SetValue(null, originalShared);
            if (original != null) File.WriteAllBytes(path, original);
            else if (File.Exists(path)) File.Delete(path);
        }
    }
}
