using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;


namespace AppMare_Maui.PopUps;

public partial class PaypalPopup : ContentPage
{

	public Page CurrentPage { get; set; }
	bool stop = false;
	bool ComingFromPaypal= false;
	public static string _paypalUrl;
	public bool donepageOpened = false;
	public double price = 0;
	public string PdtToken = "";
	public string BusinessEmail = "";
	public string Currency_Code = "";
	public bool IsSub = false;
	public TaskCompletionSource<Dictionary<string, object>> tcs = new();
	public PaypalPopup(Page pg, string _BusinessEmail, string _Currency_Code, string RenewUnit, string amount,bool IsSub,string _PdtToken)
	{
		price = double.Parse(amount);
		CurrentPage = pg;
		PdtToken = _PdtToken;
		price = double.Parse( amount);
		BusinessEmail = _BusinessEmail;
		Currency_Code = _Currency_Code;
		this.IsSub = IsSub;
	


		InitializeComponent();
		//   PayPalWebView.HandlerChanged += (s, e) =>
        // {
        //     if( PayPalWebView.Handler?.PlatformView is WebKit.WKWebView nativeWebView)
        //     {
        //         var config = nativeWebView.Configuration;
        //         config.WebsiteDataStore = WebKit.WKWebsiteDataStore.NonPersistentDataStore; // private mode
        //     }
        // };
		
	
		var url = MakePayPalUrl(IsSub, BusinessEmail, Currency_Code, amount, RenewUnit, useSandbox:false);

		PayPalWebView.Source = url;
	}
public static string MakePayPalUrl(
        bool isSub,
        string businessEmail,
        string currency,
        string amount,
        string renewUnit,
        string itemName = "",
        bool useSandbox = true)
    {
        // Choose base URL based on environment
        var baseUrl = useSandbox
            ? "https://www.sandbox.paypal.com/cgi-bin/webscr"
            : "https://www.paypal.com/cgi-bin/webscr";
        
        _paypalUrl = baseUrl;

        // Return URLs - these will receive the transaction data
        var returnUrl = useSandbox 
            ? "https://appmare.com" 
            : "https://appmare.com";
        var cancelUrl = useSandbox 
            ? "https://appmare.com" 
            : "https://appmare.com";

        if (isSub)
        {
            // Subscription payment with PDT-compatible parameters
            var renewCode = GetRenewUnitCode(renewUnit);
            return $"{baseUrl}?cmd=_xclick-subscriptions" +
                   $"&business={Uri.EscapeDataString(businessEmail)}" +
                   $"&currency_code={currency}" +
                   $"&no_note=1&src=1&sra=1" +
                   $"&a3={amount}&p3=1&t3={renewCode}" +
                   $"&item_name={Uri.EscapeDataString(itemName)}" +
                   $"&return={Uri.EscapeDataString(returnUrl)}" +
                   $"&cancel_return={Uri.EscapeDataString(cancelUrl)}" +
                   $"&rm=2"; // Important: rm=2 for POST data transfer
        }
        else
        {
            // One-time payment with PDT-compatible parameters
            return $"{baseUrl}?cmd=_xclick" +
                   $"&business={Uri.EscapeDataString(businessEmail)}" +
                   $"&amount={amount}" +
                   $"&currency_code={currency}" +
                   $"&item_name={Uri.EscapeDataString(itemName)}" +
                   $"&return={Uri.EscapeDataString(returnUrl)}" +
                   $"&cancel_return={Uri.EscapeDataString(cancelUrl)}" +
                   $"&no_shipping=1&no_note=1" +
                   $"&rm=2"; // Important: rm=2 for POST data transfer
        }
    }


    private static string GetRenewUnitCode(string renewUnit)
    {
        return renewUnit.ToLower() switch
        {
            "day" or "d" => "D",
            "week" or "w" => "W",
            "month" or "m" => "M",
            "year" or "y" => "Y",
            _ => "M" // Default to monthly
        };
    }

	public async Task<Dictionary<string, object>> MakePayment()
	{
return	await	MainThread.InvokeOnMainThreadAsync<Dictionary<string, object>>( async() =>
		{

#if BROWSER
		// Browser target: open PayPal in a popup via JavaScript interop
		var paypalUrl = MakePayPalUrl(IsSub, BusinessEmail, Currency_Code, price.ToString(), "", useSandbox: false);
		
		PayPalInterop.InitPaypalHandler("appmare.com");
		PayPalInterop.OpenPaypalPopup(paypalUrl);
		
		// Poll for result
		_ = Task.Run(async () =>
		{
			for (int i = 0; i < 600; i++)
			{
				await Task.Delay(1000);
				var tx = PayPalInterop.PollResult();
				PayPalInterop.ClearResult();
				
				if (tx == "POPUP_BLOCKED" || tx == "CLOSED" || tx == "NO_TX")
				{
					tcs.TrySetResult(null);
					return;
				}
				
				if (!string.IsNullOrEmpty(tx))
				{
					var verify = await VerifyTransactionAsync(tx);
					var lines = verify.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
					if (lines.Count > 0 && lines[0] == "SUCCESS")
					{
						var list = lines.Where(p => p.Contains("=")).ToList();
						var status = HttpUtility.UrlDecode(list.Find(k => k.Contains("payment_status")).Split("=")[1]);
						var amount = double.Parse(HttpUtility.UrlDecode(list.Find(k => k.Contains("mc_gross")).Split("=")[1]));
						var currency = HttpUtility.UrlDecode(list.Find(k => k.Contains("mc_currency")).Split("=")[1]);
						var recevieremail = HttpUtility.UrlDecode(list.Find(k => k.Contains("receiver_email")).Split("=")[1]);
						var date = ParsePayPalTimestampToUtc(HttpUtility.UrlDecode(list.Find(k => k.Contains("payment_date")).Split("=")[1]));
						var currentdate = GetWebTime();
						var diff = currentdate - date;
						
						if (status == "Completed" && Math.Abs(amount - price) < 0.01 && currency == Currency_Code && recevieremail == BusinessEmail && diff.TotalMinutes < 10)
						{
							tcs.TrySetResult(new Dictionary<string, object>
							{
								{"TransactionId", tx},
								{"TransactionDate", date.ToString()}
							});
							return;
						}
					}
					tcs.TrySetResult(null);
					return;
				}
			}
			tcs.TrySetResult(null);
		});
		
		return await tcs.Task;
#else
		await CurrentPage.Navigation.PushModalAsync(new NavigationPage(this));
		var result = await tcs.Task;

		if (CurrentPage.Navigation.ModalStack.Count > 0)
		{
			await CurrentPage.Navigation.PopModalAsync();
		}
		return result;
#endif
});
	}



	// private bool IsAllowedDomain(string url)
	// {
	//     var allowedDomains = new[]
	//     {
	//         // Core PayPal
	//         "paypal.com", "sandbox.paypal.com", "checkout.paypal.com",
	//         "api-m.paypal.com", "api-m.sandbox.paypal.com",
	//         "paypalobjects.com", "paypalstatic.com",
	//         "mobile.paypal.com", "m.paypal.com", "pp.me", "paypal.me",

	//         // Security/Verification
	//         "recaptcha.net", "www.recaptcha.net",
	//         "gstatic.com", "www.gstatic.com",
	//         "googleapis.com",

	//         // CDNs
	//         "akamaized.net", "akamaihd.net", "msecnd.net", "azureedge.net",
	//         "cloudfront.net", "cloudflaressl.com",

	//         // PayPal subsidiaries
	//         "braintreegateway.com", "braintree-api.com", "venmo.com",

	//         // Financial verification
	//         "yodlee.com", "plaid.com", "trustly.com"
	//     };

	//     return allowedDomains.Any(domain => 
	//         url.Contains($"://{domain}") || 
	//         url.Contains($"://www.{domain}") ||
	//         url.Contains($"//{domain}"));
	// }	
	// private async void CheckPaymentCompletion()
	// {
	// 	if (!stop)
	// 	{


	// 		try
	// 		{
	// 			var readyState = await PayPalWebView.EvaluateJavaScriptAsync("document.readyState");

	// 			if (readyState?.ToString() == "complete")
	// 			{
	// 				var html = await PayPalWebView.EvaluateJavaScriptAsync("document.documentElement.outerHTML");
	// 				if (html?.ToString().Contains("WPSDonePage_container") == true ||
	// 					html?.ToString().Contains("donepage") == true)
	// 				{
	// 					tcs.TrySetResult(true);
	// 					return;
	// 				}
	// 			}

	// 			// If not ready or success not found, maybe retry after delay
	// 			await Task.Delay(500);
	// 			CheckPaymentCompletion(); // Recursive retry (add max retry counter)
	// 		}
	// 		catch (Exception ex)
	// 		{
	// 			// Log error, but don't fail payment detection
	// 		}
	// 	}
	// }
	public async Task<string> DoJavascript(string code)
	{
#if IOS || MACCATALYST
		var resultios = await (PayPalWebView.Handler.PlatformView as WebKit.WKWebView).EvaluateJavaScriptAsync(code);
		var result = resultios.ToString();
		return result;
#else
var result = await PayPalWebView.EvaluateJavaScriptAsync(code);
return result;
#endif

	}

	// private async void CheckPaymentSubscribtionCompletion()
	// {
	// 	if (!stop)
	// 	{
	// 		try
	// 		{
	// 			// Get the ready state of the page
			

			
	// 						var readyState = await DoJavascript("document.readyState");


	// 			if (readyState == "complete")
	// 			{
	// 				// Get the HTML of the page
	// 				var html = await DoJavascript("document.documentElement.outerHTML");
	// 				if (html?.ToString().Contains("subscriptionsDonePage") == true ||
	// 					html?.ToString().Contains("donepage") == true || html?.ToString().Contains(@"id=""receipt-circle""") == true || html?.ToString().Contains(@"id=""receipt-paid-text""") == true)
	// 				{
	// 					tcs.TrySetResult(true);
	// 					return;
	// 				}
	// 			}

	// 				// Check if the page contains the success text
	// 			// If not ready or success not found, maybe retry after delay
	// 			await Task.Delay(500);
	// 			CheckPaymentCompletion(); // Recursive retry (add max retry counter)
	// 		}
	// 		catch (Exception ex)
	// 		{
	// 			// Log error, but don't fail payment detection
	// 		}
	// 	}
	// }
/*******  143020fd-cf6a-4550-a39b-0a418e65e5fa  *******/
	  private static string ExtractTransactionIdManually(string url)
    {
        try
        {
            // Find tx parameter
            var txIndex = url.IndexOf("tx=", StringComparison.OrdinalIgnoreCase);
            if (txIndex == -1)
                return null;

            // Start after "tx="
            var startIndex = txIndex + 3;
            
            // Find the end (next & or end of string)
            var endIndex = url.IndexOf('&', startIndex);
            if (endIndex == -1)
                endIndex = url.Length;

            // Extract and decode the transaction ID
            var transactionId = url.Substring(startIndex, endIndex - startIndex);
            return HttpUtility.UrlDecode(transactionId);
        }
        catch (Exception)
        {
            return null;
        }
    }
 public static string GetTransactionId(string url)
	{
		if (string.IsNullOrEmpty(url))
			return null;

		try
		{
			// Create Uri object to parse the URL
			var uri = new Uri(url);

			// Parse query string parameters
			var queryParams = HttpUtility.ParseQueryString(uri.Query);

			// Return the tx (transaction ID) value
			return queryParams["tx"];
		}
		catch (Exception)
		{
			// If URL parsing fails, try manual extraction
			return ExtractTransactionIdManually(url);
		}
	}
	 public async Task<string> VerifyTransactionAsync(string transactionId)
    {
        try
        {
		var 	_httpClient = new HttpClient();
            // Prepare the form data
			var formData = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("cmd", "_notify-synch"),
                new KeyValuePair<string, string>("tx", transactionId),
                new KeyValuePair<string, string>("at", PdtToken)
            };

            // Create form content
            var formContent = new FormUrlEncodedContent(formData);

            // Send POST request
            var response = await _httpClient.PostAsync(_paypalUrl, formContent);
            
            // Read response
            var responseContent = await response.Content.ReadAsStringAsync();
            
            return responseContent;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error verifying PayPal transaction: {ex.Message}", ex);
        }
    }
	    public static DateTime GetWebTime()
    {
        try
        {
            var client = GuerrillaNtp.NtpClient.Default;
            var clock = client.Query();
            return clock.UtcNow.UtcDateTime;
        }
        catch
        {
			return DateTime.UtcNow;
        }
    }
public static DateTime ParsePayPalTimestampToUtc(string paypalTimestamp)
{
    try
    {
        var parts = paypalTimestamp.Split(' ');
        var time = parts[0];
        var month = parts[1]; 
        var day = parts[2].TrimEnd(',');
        var year = parts[3];
        // parts[4] is always "PDT" according to PayPal docs

        var dateTimeString = $"{month} {day}, {year} {time}";
        var localDateTime = DateTime.ParseExact(
            dateTimeString,
            "MMM d, yyyy HH:mm:ss", 
            CultureInfo.InvariantCulture
        );

        // PDT is always UTC-7 according to PayPal
        return localDateTime.AddHours(7); // Convert PDT to UTC
    }
    catch
    {
        throw new ArgumentException($"Invalid PayPal timestamp: {paypalTimestamp}");
    }
}
public bool ValidatePayPalPDTResponse(string[] lines)
{
    try
    {
        // Check if response indicates success
        if (lines?.Length == 0 || lines[0] != "SUCCESS")
        {
            return false;
        }

        // Parse response into key-value pairs
        var responseData = lines
            .Where(p => !string.IsNullOrWhiteSpace(p) && p.Contains("="))
            .ToDictionary(
                line => line.Split('=')[0]?.Trim().ToLowerInvariant(),
                line => string.Join("=", line.Split('=').Skip(1)) // Handle values with '=' in them
            );

        // Required fields validation
        var requiredFields = new[] { "payment_status", "receiver_email", "mc_gross", "mc_currency", "payment_date" };
        foreach (var field in requiredFields)
        {
            if (!responseData.ContainsKey(field) || string.IsNullOrWhiteSpace(responseData[field]))
            {
                return false;
            }
        }

        // Extract values with URL decoding
        var status = HttpUtility.UrlDecode(responseData["payment_status"]);
        var receiverEmail = HttpUtility.UrlDecode(responseData["receiver_email"]);
        var currency = HttpUtility.UrlDecode(responseData["mc_currency"]);
        var grossAmountStr = HttpUtility.UrlDecode(responseData["mc_gross"]);
        var paymentDateStr = HttpUtility.UrlDecode(responseData["payment_date"]);

        // Parse and validate amount
        if (!double.TryParse(grossAmountStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
        {
            return false;
        }

        // Parse payment date
        var paymentDate = ParsePayPalTimestampToUtc(paymentDateStr);
        // if (!paymentDate.HasValue)
        // {
        //     return false;
        // }

        // Get current time
        var currentDate = GetWebTime();
			var timeDifference = currentDate - paymentDate;

        // Comprehensive validation checks
        var validationChecks = new[]
        {
            // Payment must be completed
            status.Equals("Completed", StringComparison.OrdinalIgnoreCase),
            
            // Amount must match exactly (consider using decimal for money)
            Math.Abs(amount - price) < 0.01, // Small tolerance for floating point comparison
            
            // Currency must match
            currency.Equals(Currency_Code, StringComparison.OrdinalIgnoreCase),
            
            // Receiver email must match (case insensitive)
            receiverEmail.Equals(BusinessEmail, StringComparison.OrdinalIgnoreCase),
            
            // Payment must be recent (within 10 minutes is more reasonable than 3)
            timeDifference.TotalMinutes >= 0 && timeDifference.TotalMinutes < 10,
            
            // Additional security: Check transaction isn't too old (prevent replay attacks)
            timeDifference.TotalHours < 24
        };

        // Optional: Add transaction ID validation to prevent duplicate processing
        if (responseData.ContainsKey("txn_id"))
        {
            var transactionId = HttpUtility.UrlDecode(responseData["txn_id"]);
            if (!string.IsNullOrWhiteSpace(transactionId))
            {
                // You should implement this method to check against your database
                // validationChecks = validationChecks.Append(!IsTransactionAlreadyProcessed(transactionId)).ToArray();
            }
        }

        // All checks must pass
        return validationChecks.All(check => check);
    }
    catch (Exception ex)
    {
        // Log the exception for debugging
        // Logger.LogError(ex, "Error validating PayPal PDT response");
        return false;
    }
}

// Alternative version with more detailed error reporting
public class PayPalValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new List<string>();
    public Dictionary<string, string> ParsedData { get; set; } = new Dictionary<string, string>();
}

public PayPalValidationResult ValidatePayPalPDTResponseDetailed(string[] lines)
{
    var result = new PayPalValidationResult();

    try
    {
        if (lines?.Length == 0 || lines[0] != "SUCCESS")
        {
            result.Errors.Add("PayPal response does not indicate success");
            return result;
        }

        // Parse response
        var responseData = lines
            .Where(p => !string.IsNullOrWhiteSpace(p) && p.Contains("="))
            .ToDictionary(
                line => line.Split('=')[0]?.Trim().ToLowerInvariant(),
                line => HttpUtility.UrlDecode(string.Join("=", line.Split('=').Skip(1)))
            );

        result.ParsedData = responseData;

        // Validate required fields
        var requiredFields = new[] { "payment_status", "receiver_email", "mc_gross", "mc_currency", "payment_date" };
        var missingFields = requiredFields.Where(field => !responseData.ContainsKey(field) || string.IsNullOrWhiteSpace(responseData[field])).ToList();
        
        if (missingFields.Any())
        {
            result.Errors.Add($"Missing required fields: {string.Join(", ", missingFields)}");
            return result;
        }

        // Individual validations with specific error messages
        if (!responseData["payment_status"].Equals("Completed", StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add($"Payment status is '{responseData["payment_status"]}', expected 'Completed'");
        }

        if (!double.TryParse(responseData["mc_gross"], NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
        {
            result.Errors.Add($"Invalid amount format: {responseData["mc_gross"]}");
        }
        else if (Math.Abs(amount - price) >= 0.01)
        {
            result.Errors.Add($"Amount mismatch: received {amount}, expected {price}");
        }

        if (!responseData["mc_currency"].Equals(Currency_Code, StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add($"Currency mismatch: received '{responseData["mc_currency"]}', expected ''");
        }

        if (!responseData["receiver_email"].Equals(BusinessEmail, StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add($"Receiver email mismatch: received '{responseData["receiver_email"]}', expected ''");
        }

        var paymentDate =ParsePayPalTimestampToUtc(responseData["payment_date"]);
     
            var currentDate = GetWebTime();
			var timeDifference = currentDate - paymentDate;

            if (timeDifference.TotalMinutes < 0)
            {
                result.Errors.Add("Payment date is in the future");
            }
            else if (timeDifference.TotalMinutes >= 10)
            {
                result.Errors.Add($"Payment is too old: {timeDifference.TotalMinutes:F1} minutes ago");
            }

            if (timeDifference.TotalHours >= 24)
            {
                result.Errors.Add("Payment is more than 24 hours old");
            }
        

        result.IsValid = !result.Errors.Any();
        return result;
    }
    catch (Exception ex)
    {
        result.Errors.Add($"Validation error: {ex.Message}");
        return result;
    }
}

	private async void PayPalWebView_Navigated(object sender, WebNavigatedEventArgs e)
	{
		if ((e.Url.StartsWith("https://www.paypal.com/") || e.Url.StartsWith("https://www.sandbox.paypal.com/")))
		{
			ComingFromPaypal = true;


		}
		else if (e.Url.StartsWith("https://appmare.com/") && ComingFromPaypal)
		{

			var transactionId = GetTransactionId(e.Url);
			if (transactionId != null)
			{
				var verify = await VerifyTransactionAsync(transactionId);
				List<string> lines = verify
		 .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
		 .ToList();
		 var validate = ValidatePayPalPDTResponse(lines.ToArray());
                if (lines[0] == "SUCCESS")
                {
                    var list = lines.Where(p => p.Contains("=")).ToList();






                    var status = HttpUtility.UrlDecode(list.Find(k => k.Contains("payment_status")).Split("=")[1]);
                    var recevieremail = HttpUtility.UrlDecode(list.Find(k => k.Contains("receiver_email")).Split("=")[1]);
                    var amount = double.Parse(HttpUtility.UrlDecode(list.Find(k => k.Contains("mc_gross")).Split("=")[1]));
                    var currency = HttpUtility.UrlDecode(HttpUtility.UrlDecode(list.Find(k => k.Contains("mc_currency")).Split("=")[1]));
                    var date = (ParsePayPalTimestampToUtc(HttpUtility.UrlDecode(list.Find(k => k.Contains("payment_date")).Split("=")[1])));
                    var currentdate = GetWebTime();
                    var diff = currentdate - date;
                    if (status == "Completed" && amount == price && currency == Currency_Code && recevieremail == BusinessEmail && diff.TotalMinutes < 3)
                    {
                        var dic = new Dictionary<string, object>()
                        {
                            {"TransactionId",transactionId},
                            {"TransactionDate",date.ToString()},

                        };
                      

                        tcs.TrySetResult(dic);

                    }
                    else
                    {
                        tcs.TrySetResult(null);
                    }


				}
				// tcs.TrySetResult(transactionId);
			}

			ComingFromPaypal = false;
		}
		else
		{
			ComingFromPaypal = false;
		}

		// if (e.Url.StartsWith("https://www.paypal.com/") || e.Url.StartsWith("https://www.sandbox.paypal.com/"))
		// {
		// 	if (payWithPaypal.Type == PaypalType.Payment)
		// 	{

		// 		stop = false;

		// 		CheckPaymentCompletion();



		// 	}



		// 	else
		// 	{
		// 		if (e.Result == WebNavigationResult.Success)
		// 		{
		// 			stop = false;

		// 			CheckPaymentSubscribtionCompletion();

		// 		}

		// 	}



		// }
		// 		else if (e.Url.StartsWith("https://www.appmare.com"))
		// 	{
		// 		e.Source

		// 		if ()
		// 	{

		// 		tcs.TrySetResult(true);
		// 	}


		//  }
		// else
		// {
		// 	stop = true;
		// }
	}

	private void BackButton_Clicked(object sender, EventArgs e)
	{
		if (PayPalWebView.CanGoBack)
		{
			PayPalWebView.GoBack();
		}
    }

	private void ForwardButton_Clicked(object sender, EventArgs e)
	{
		if (PayPalWebView.CanGoForward)
		{
			PayPalWebView.GoForward();
		}
    }

	private void ReloadButton_Clicked(object sender, EventArgs e)
	{
			PayPalWebView.Reload();
    }

	private void CloseButton_Clicked(object sender, EventArgs e)
	{
		tcs.TrySetResult(null);
    }
}

#if BROWSER
[SupportedOSPlatform("browser")]
public static partial class PayPalInterop
{
	[JSImport("globalThis.localStorage.getItem")]
	public static partial string LocalStorageGet(string key);

	[JSImport("globalThis.localStorage.removeItem")]
	public static partial void LocalStorageRemove(string key);

	[JSImport("globalThis.eval")]
	public static partial void Eval(string code);

	public static void InitPaypalHandler(string returnDomain)
	{
		var js = @"
(function() {
    var pp = null;
    window.__paypalStart = function(url) {
        pp = window.open(url, 'paypal_checkout', 'width=600,height=700,scrollbars=yes,menubar=no,toolbar=no,location=no');
        if (!pp) { localStorage.setItem('paypal_tx', 'POPUP_BLOCKED'); return; }
        poll();
    };
    function poll() {
        try {
            if (pp.closed) { localStorage.setItem('paypal_tx', 'CLOSED'); return; }
            var href = pp.location.href;
            if (href && href.indexOf('" + returnDomain + @"') >= 0) {
                var m = href.match(/[?&]tx=([^&]+)/);
                var tx = m ? m[1] : '';
                localStorage.setItem('paypal_tx', tx || 'NO_TX');
                pp.close();
                return;
            }
        } catch(e) {}
        setTimeout(poll, 500);
    }
})();
";
		Eval(js);
	}

	public static void OpenPaypalPopup(string url)
	{
		Eval($"window.__paypalStart('{url}')");
	}
	
	public static string PollResult()
	{
		try { return LocalStorageGet("paypal_tx"); } catch { return null; }
	}
	
	public static void ClearResult()
	{
		try { LocalStorageRemove("paypal_tx"); } catch { }
	}
}
#endif