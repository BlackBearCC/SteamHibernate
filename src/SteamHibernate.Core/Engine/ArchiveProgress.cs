// src/SteamHibernate.Core/Engine/ArchiveProgress.cs
namespace SteamHibernate.Core.Engine;

public sealed record ArchiveProgress(string Stage, double Fraction);
