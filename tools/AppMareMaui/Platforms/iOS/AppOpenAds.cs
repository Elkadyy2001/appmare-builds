using System;
using AppMare_Maui;
using Foundation;

using MT.Admob;
using SampleApp;
using UIKit;

namespace YourAppNamespace
{
    public class AppOpenAdManager
    {
        private static AppOpenAd appOpenAd;


        public static void ShowAD()
        {
            var adRequest = Request.GetDefaultRequest();
         
            AppOpenAd.Load(AdmobAD.appopen, adRequest, AppOpenAdLoaded);
        }
        
        private static void AppOpenAdLoaded(AppOpenAd ad, NSError error)
        {
            if (error != null)
            {
                Console.WriteLine("Failed to load app open ad: " + error);
                return;
            }

            appOpenAd = ad;

            appOpenAd.PresentFromRootViewController(GetTopViewController());

        }

     
        public static UIViewController GetTopViewController()
        {
            var window = UIApplication.SharedApplication.KeyWindow;
            var vc = window.RootViewController;
            while (vc.PresentedViewController != null)
                vc = vc.PresentedViewController;

            if (vc is UINavigationController navController)
                vc = navController.ViewControllers.Last();

            return vc;
        }
     
    }
}

