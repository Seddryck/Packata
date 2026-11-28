namespace Packata.Ossie;

/// <summary>Selects a dataset for publication with an optional deployment relation name.</summary>
public sealed record OssieDatasetPublication(string DatasetName, string? PhysicalName = null);
