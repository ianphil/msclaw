using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using MsClaw.Gateway.Services.Tools;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class ToolDescriptorTests
{
    [Fact]
    public void ToolDescriptor_HasRequiredMetadataAndDefaults()
    {
        var descriptorType = typeof(ToolDescriptor);
        var instance = Assert.IsType<ToolDescriptor>(Activator.CreateInstance(descriptorType, nonPublic: true));

        Assert.True(descriptorType.IsSealed);
        Assert.NotNull(descriptorType.GetMethod("PrintMembers", BindingFlags.Instance | BindingFlags.NonPublic));

        var functionProperty = descriptorType.GetProperty(nameof(ToolDescriptor.Function));
        Assert.NotNull(functionProperty);
        Assert.Equal(typeof(AIFunction), functionProperty.PropertyType);
        Assert.NotNull(functionProperty.GetCustomAttribute<RequiredMemberAttribute>());

        var providerNameProperty = descriptorType.GetProperty(nameof(ToolDescriptor.ProviderName));
        Assert.NotNull(providerNameProperty);
        Assert.Equal(typeof(string), providerNameProperty.PropertyType);
        Assert.NotNull(providerNameProperty.GetCustomAttribute<RequiredMemberAttribute>());

        var tierProperty = descriptorType.GetProperty(nameof(ToolDescriptor.Tier));
        Assert.NotNull(tierProperty);
        Assert.Equal(typeof(ToolSourceTier), tierProperty.PropertyType);
        Assert.NotNull(tierProperty.GetCustomAttribute<RequiredMemberAttribute>());

        var alwaysVisibleProperty = descriptorType.GetProperty(nameof(ToolDescriptor.AlwaysVisible));
        Assert.NotNull(alwaysVisibleProperty);
        Assert.Equal(typeof(bool), alwaysVisibleProperty.PropertyType);
        Assert.False(instance.AlwaysVisible);
    }
}
