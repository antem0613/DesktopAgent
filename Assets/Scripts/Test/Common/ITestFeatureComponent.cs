public interface ITestFeatureComponent
{
    bool IsTestEnabled { get; }
    void OnTestStart();
    void OnTestTick(float deltaTime);
    void OnTestStop();
}