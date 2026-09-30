using System.Collections.ObjectModel;
using AstroForge.Core.Models;

namespace AstroForge.App.ViewModels;

public sealed class ProjectTreeNode : BindableBase
{
    private bool _isExpanded;
    private bool _isMarked;

    public required string Key { get; init; }
    public required string Name { get; init; }
    public string Detail { get; init; } = "";
    public string Icon { get; init; } = "◇";
    public ObservableCollection<ProjectTreeNode> Children { get; } = [];
    public required IReadOnlyList<FrameMetadata> Frames { get; init; }
    public int Count => Frames.Count;
    public int IssueCount => Frames.Sum(frame => frame.Issues.Count);
    public bool HasIssues => IssueCount > 0;
    public bool IsLeaf => Children.Count == 0;
    public bool CanBeMarked => Key.StartsWith("night:", StringComparison.Ordinal) || Key.StartsWith("optical-session:", StringComparison.Ordinal) || Key.StartsWith("project-filter:", StringComparison.Ordinal) || Key.StartsWith("series:", StringComparison.Ordinal);
    public bool IsMarked { get => _isMarked; set => Set(ref _isMarked, value); }
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
}
