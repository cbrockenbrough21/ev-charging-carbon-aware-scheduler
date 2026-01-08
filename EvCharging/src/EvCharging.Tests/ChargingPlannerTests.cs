using EvCharging.Core.Planner;

namespace EvCharging.Tests
{
    public class ChargingPlannerTests
    {
        [Fact]
        public void ChargingPlanner_CanBeInstantiated()
        {
            // Arrange & Act
            ChargingPlanner planner = new ChargingPlanner();

            // Assert
            Assert.NotNull(planner);
        }
    }
}
