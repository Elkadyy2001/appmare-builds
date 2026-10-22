using System;
using Android.Runtime;
using static Android.Gms.Ads.AppOpen.AppOpenAd;

namespace Android.Gms.Ads.Workarounds;

/// <summary>
/// Temporary workaround until the bindings libraries are updated.
/// Source: https://github.com/xamarin/GooglePlayServicesComponents/issues/425
/// </summary>
/// 

public abstract class AppOpenAdLoadCallback : Android.Gms.Ads.AppOpen.AppOpenAd.AppOpenAdLoadCallback
{
    [Register("onAdLoaded", "(Lcom/google/android/gms/ads/appopen/AppOpenAd;)V", "GetOnAdLoadedHandler")]
    public virtual void OnAdLoaded(Android.Gms.Ads.AppOpen.AppOpenAd appOpenAd)
    {

    }

    private static Delegate cb_onAdLoaded;

    private static Delegate GetOnAdLoadedHandler()
    {
        if (cb_onAdLoaded is null)
            cb_onAdLoaded = JNINativeWrapper.CreateDelegate((Action<IntPtr, IntPtr, IntPtr>)n_onAdLoaded);
        return cb_onAdLoaded;
    }

    private static void n_onAdLoaded(IntPtr jnienv, IntPtr native__this, IntPtr native_p0)
    {

        AppOpenAdLoadCallback thisobject = GetObject<AppOpenAdLoadCallback>(jnienv, native__this, JniHandleOwnership.DoNotTransfer);
        Android.Gms.Ads.AppOpen.AppOpenAd resultobject = GetObject<AppOpen.AppOpenAd>(native_p0, JniHandleOwnership.DoNotTransfer);
        thisobject.OnAdLoaded(resultobject);
    }
}

