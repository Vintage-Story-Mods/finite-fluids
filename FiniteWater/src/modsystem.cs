using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace FiniteWater
{
    public class FiniteWaterModSystem : ModSystem
    {
        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.RegisterBlockBehaviorClass("TrueFiniteSpreadingLiquid", typeof(BlockBehaviorTrueFiniteSpreadingLiquid));
            api.RegisterBlockClass("finiteBlockWater", typeof(finiteBlockWater));
            api.RegisterBlockClass("finiteBlockWaterfall", typeof(finiteBlockWaterfall));
            api.RegisterBlockClass("finiteBlockWaterflowing", typeof(finiteBlockWaterflowing));
        }

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);
            
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);

        }
    }
}