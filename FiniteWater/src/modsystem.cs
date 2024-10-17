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

            // Register block behaviors
            api.RegisterBlockBehaviorClass("TrueFiniteSpreadingLiquid", typeof(BlockBehaviorTrueFiniteSpreadingLiquid));

            // Register blocks
            api.RegisterBlockClass("finiteBlockWater", typeof(finiteBlockWater));
            api.RegisterBlockClass("finiteBlockWaterfall", typeof(finiteBlockWaterfall));
            api.RegisterBlockClass("finiteBlockWaterflowing", typeof(finiteBlockWaterflowing));
            // Register other necessary components here
        }

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);

            // You can use this method to ensure custom assets are correctly linked
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

            // Server-specific initialization
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);

            // Client-specific initialization
        }
    }
}