namespace NoOceanTranslocators;

public class NoOceanTranslocatorsConfig
{
    public static NoOceanTranslocatorsConfig Loaded { get; set; } = new NoOceanTranslocatorsConfig();

    //Minimum value in ocean map to consider the chunk ocean (0-255). At 0, every chunk will be considered ocean.
    public int oceanThreshold = 128;

    //Every time we generate a chunk that's in the ocean, increase the max search range of the translocator by this many blocks.
    public int failMaxRangeIncrease = 200;
    
    //Every time we generate a chunk that's in the ocean, increase the min search range of the translocator by this many blocks.
    public int failMinRangeIncrease = 0;

    //Every time we generate a chunk that's in the ocean, accept it anyway at these odds (0.0-1.0).
    public float oceanAcceptChance = 0.00f;
}