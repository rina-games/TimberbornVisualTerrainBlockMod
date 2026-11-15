using Bindito.Core;
using Timberborn.SingletonSystem;

namespace Jove.VisualTerrainBlock
{
    [Context("Game")]
    public class VisualTerrainBlockConfigurator : Configurator
    {
        protected override void Configure()
        {
            Bind<LoadCleanupPatch>().AsSingleton();
            Bind<ExplosionPatch>().AsSingleton();
        }
    }
}
