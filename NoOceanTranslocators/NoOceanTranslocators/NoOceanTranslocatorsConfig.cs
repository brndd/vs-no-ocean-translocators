namespace NoOceanTranslocators;

public class NoOceanTranslocatorsConfig
{
    public static NoOceanTranslocatorsConfig Loaded { get; set; } = new NoOceanTranslocatorsConfig();

    //Minimum value in ocean map to consider the chunk ocean (0-255). At, 0 every chunk will be considered ocean.
    public int oceanThreshold = 200;

    //Every time we generate a chunk that's in the ocean, increase the search range of the translocator by this many blocks.
    public int failRangeIncrease = 200;

    //Every time we generate a chunk that's in the ocean, accept it anyway at these odds (0.0-1.0).
    public float oceanAcceptChance = 0.05f;
}