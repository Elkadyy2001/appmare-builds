namespace AppMare_Maui;
#if IOS || ANDROID
public class AdmobAD
{

    public static string inter = string.Empty;
    public static string appopen = string.Empty;
    public bool isadshowing = false;
  Plugin.MauiMTAdmob.Interfaces.IMTAdmob ad;
    public AdmobAD(){
        ad =  Plugin.MauiMTAdmob.CrossMauiMTAdmob.Current;
ad.OnInterstitialLoaded +=Current_OnInterstitialLoaded;
ad.OnInterstitialFailedToLoad +=OnInterstitialFailed;
ad.OnRewardedLoaded += Current_OnRewardLoaded;
ad.OnRewardedFailedToLoad += OnRewardFailed;
    }
    public  TaskCompletionSource<bool>  IsinterLoaded = new TaskCompletionSource<bool>();
public  TaskCompletionSource<bool>  IsRewardLoaded = new TaskCompletionSource<bool>();

 private void Current_OnRewardLoaded(object sender, EventArgs e)
        {
                              
               IsRewardLoaded.TrySetResult(true);
                 
        }
        private void OnRewardFailed(object sender, EventArgs e)
        {
                              
                IsRewardLoaded.TrySetResult(false);
                 
        }
 private void Current_OnInterstitialLoaded(object sender, EventArgs e)
        {
                              
                IsinterLoaded.TrySetResult(true);
                 
        }
        private void OnInterstitialFailed(object sender, EventArgs e)
        {
                              
                IsinterLoaded.TrySetResult(false);
                 
        }
        
public  async Task<bool> ShowReward(string adid){
  if ( isadshowing == false){
isadshowing = true;


 
  ad.LoadRewarded(adid);
 var ad2 = await IsRewardLoaded.Task;
 IsRewardLoaded = new();
 isadshowing = false;
 if (ad2 == true){
ad.ShowRewarded();
return true;
}
else{
    return false;
}
 }
return  false;



}
public  async Task<bool> ShowInterstitial(string adid){
  if ( isadshowing == false){

isadshowing =true;


  ad.LoadInterstitial(adid);
 var ad2 = await IsinterLoaded.Task;
 IsinterLoaded = new();
 isadshowing = false;
 if (ad2 == true){
ad.ShowInterstitial();
return true;
}
else{
    return false;
}
 }
return  false;



}


}
#endif