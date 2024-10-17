using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace FiniteWater;

public class BlockEntityLiquid : BlockEntity
{
    public float PartialVolume { get; set; }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat("partialVolume", PartialVolume);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        PartialVolume = tree.GetFloat("partialVolume", 0f);
    }
}