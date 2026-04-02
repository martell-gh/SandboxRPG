namespace MTEngine.Core;

public interface IPrototypeInitializable
{
    void InitializeFromPrototype(EntityPrototype proto, AssetManager assets);
}