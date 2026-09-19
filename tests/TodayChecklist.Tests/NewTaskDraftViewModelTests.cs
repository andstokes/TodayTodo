using TodayChecklist.Models;
using TodayChecklist.ViewModels;

namespace TodayChecklist.Tests;

[TestClass]
public sealed class NewTaskDraftViewModelTests
{
    [TestMethod]
    public void Begin_CreatesActiveEmptyDraft()
    {
        var draft = new NewTaskDraftViewModel();

        draft.Begin();

        Assert.IsTrue(draft.IsActive);
        Assert.IsFalse(draft.HasContent);
        Assert.IsFalse(draft.NeedsResolution);
    }

    [TestMethod]
    public void NotesOrRepeat_CountAsUnsavedContent()
    {
        var draft = new NewTaskDraftViewModel { IsActive = true, Notes = "备注" };
        Assert.IsTrue(draft.HasContent);
        Assert.IsTrue(draft.NeedsResolution);

        draft.Notes = string.Empty;
        draft.RepeatKind = RepeatKind.Weekly;
        Assert.IsTrue(draft.HasContent);
    }

    [TestMethod]
    public void Reset_ClearsEveryDraftField()
    {
        var draft = new NewTaskDraftViewModel
        {
            IsActive = true,
            IsExpanded = true,
            Title = "待保存",
            Notes = "备注",
            RepeatKind = RepeatKind.Daily,
        };

        draft.Reset();

        Assert.IsFalse(draft.IsActive);
        Assert.IsFalse(draft.IsExpanded);
        Assert.IsFalse(draft.HasContent);
        Assert.AreEqual(RepeatKind.None, draft.RepeatKind);
    }
}
