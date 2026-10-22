// Plugin.MauiMTAdmob.Platforms.Android.AppOpenAdManager
using System;
using Android.App;
using Android.Content;
using Android.Gms.Ads;
using System;



using Microsoft.Maui;
using Android.Gms.Ads.AppOpen;
using Microsoft.Maui.ApplicationModel;
using AppMare_Maui;

namespace SampleApp
{

    public class AppOpenAdManagerr : Android.Gms.Ads.Workarounds.AppOpenAdLoadCallback
    {

        private Android.Gms.Ads.AppOpen.AppOpenAd appOpenAd;


        public AppOpenAdManagerr()
        {

        }
        public override void OnAdLoaded(AppOpenAd appOpenAd)
        {
            appOpenAd.Show(Platform.CurrentActivity);
            base.OnAdLoaded(appOpenAd);
        }

        public void ShowAd(Context context)
        {
            var u = new AdRequest.Builder().Build();

            AppOpenAd.Load(context, AdmobAD.appopen,u, AppOpenAd.AppOpenAdOrientationPortrait, this);
        }





    }
}

