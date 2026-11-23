namespace Packata.Core;

/// <summary>A geographic point expressed as longitude and latitude.</summary>
/// <param name="Longitude">The longitude coordinate.</param>
/// <param name="Latitude">The latitude coordinate.</param>
public readonly record struct GeoPoint(double Longitude, double Latitude);
