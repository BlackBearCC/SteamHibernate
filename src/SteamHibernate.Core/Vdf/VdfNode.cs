// src/SteamHibernate.Core/Vdf/VdfNode.cs
namespace SteamHibernate.Core.Vdf;

public sealed class VdfNode
{
    public static readonly VdfNode Empty = new();

    private readonly Dictionary<string, VdfNode> _children =
        new(StringComparer.OrdinalIgnoreCase);

    public string? Value { get; init; }
    public bool IsEmpty => Value is null && _children.Count == 0;

    public IReadOnlyDictionary<string, VdfNode> Children => _children;

    public VdfNode this[string key] =>
        _children.TryGetValue(key, out var n) ? n : Empty;

    internal void Add(string key, VdfNode node) => _children[key] = node;
}
