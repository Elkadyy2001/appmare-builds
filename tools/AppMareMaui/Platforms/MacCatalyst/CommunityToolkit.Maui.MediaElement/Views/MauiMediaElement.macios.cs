using System.ComponentModel;
using AVKit;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls.Platform.Compatibility;
using Microsoft.Maui.Platform;
using UIKit;

namespace CommunityToolkit.Maui.Core.Views;

/// <summary>
/// The user-interface element that represents the <see cref="MediaElement"/> on iOS and macOS.
/// </summary>
public class MauiMediaElement : UIView
{
	/// <summary>
	/// Initializes a new instance of the <see cref="MauiMediaElement"/> class.
	/// </summary>
	/// <param name="playerViewController">The <see cref="AVPlayerViewController"/> that acts as the platform media player.</param>
	/// <param name="parentViewController">The <see cref="UIViewController"/> that acts as the parent for <paramref name="playerViewController"/>.</param>
	/// <exception cref="NullReferenceException">Thrown when <paramref name="playerViewController"/><c>.View</c> is <see langword="null"/>.</exception>
	/// 
	UIViewController topViewControllerWithRootViewController(UIViewController rootViewController)
	{
		if (rootViewController is UITabBarController)
		{
			UITabBarController tabBarController = (UITabBarController)rootViewController;
#pragma warning disable CS8604 // Possible null reference argument.
			return topViewControllerWithRootViewController(tabBarController.SelectedViewController);
#pragma warning restore CS8604 // Possible null reference argument.
		}
		else if (rootViewController is UINavigationController)
		{
			UINavigationController navigationController = (UINavigationController)rootViewController;
			return topViewControllerWithRootViewController(navigationController.VisibleViewController);
		}
		else if (rootViewController.PresentedViewController != null)
		{
			UIViewController presentedViewController = rootViewController.PresentedViewController;
			return topViewControllerWithRootViewController(presentedViewController);
		}
		else
		{
			return rootViewController;
		}

	}
	public MauiMediaElement(AVPlayerViewController playerViewController, UIViewController? parentViewController)
	{

		ArgumentNullException.ThrowIfNull(playerViewController.View);
		playerViewController.View.Frame = Bounds;

		// On iOS 16+ and macOS 13+ the AVPlayerViewController has to be added to a parent ViewController, otherwise the transport controls won't be displayed.


		var viewController = topViewControllerWithRootViewController(UIApplication.SharedApplication.KeyWindow.RootViewController);


		// If we don't find the viewController, assume it's not Shell and still continue, the transport controls will still be displayed
		if (viewController?.View is not null)
		{
			// Zero out the safe area insets of the AVPlayerViewController
			UIEdgeInsets insets = viewController.View.SafeAreaInsets;
			playerViewController.AdditionalSafeAreaInsets =
				new UIEdgeInsets(insets.Top * -1, insets.Left, insets.Bottom * -1, insets.Right);

			// Add the View from the AVPlayerViewController to the parent ViewController
			viewController.AddChildViewController(playerViewController);
		}

		AddSubview(playerViewController.View);

	}

	/// <summary>
	/// Adds PlayerViewController to the Parent ViewController
	/// </summary>
	/// <param name="playerViewController"></param>
	/// <param name="parentViewController"></param>
	public void UpdateParentViewController(AVPlayerViewController playerViewController, UIViewController parentViewController)
	{
		parentViewController.AddChildViewController(playerViewController);

		if (playerViewController.View is not null)
		{
			AddSubview(playerViewController.View);
		}
	}
}