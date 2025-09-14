namespace GridScout2
{
  public record ScoutEntry
  {
    //Type, Corporation, Alliance, Name, Velocity
    public string? Type { get; init; }
    public string? Corporation { get; init; }
    public string? Alliance { get; init; }
    public string? Name { get; init; }
    public string? Distance { get; init; }
    public string? Velocity { get; init; }
  }
}