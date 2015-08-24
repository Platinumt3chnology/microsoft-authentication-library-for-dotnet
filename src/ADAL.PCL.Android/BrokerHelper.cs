using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Android.Accounts;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using Java.Security;
using Java.Util.Concurrent;

namespace Microsoft.IdentityModel.Clients.ActiveDirectory
{
    class BrokerHelper : IBrokerHelper
    {

        public const string AuthTokenType = "adal.authtoken.type";
        private const string RedirectUriScheme = "msauth";

        private const string BrokerPackageName = "com.microsoft.windowsintune.companyportal";
        private const string BrokerAccountType = "com.microsoft.workaccount";
        public const string AzureAuthenticatorAppSignature = "ho040S3ffZkmxqtQrSwpTVOn9r0=";
        public const string AzureAuthenticatorAppSignature2 = "1L4Z9FJCgn5c0VLhyAxC5O9LdlE=";

        public Task<AuthenticationResultEx> AcquireTokenUsingBroker(IDictionary<string, string> brokerPayload)
        {

            this.VerifyManifestPermissions();

            AccountManager accountManager = AccountManager.Get(Application.Context);

            this.VerifyBrokerApp(accountManager);
            this.AuthenticateViaBroker(accountManager, requestParameters, this.VerifyAccount(accountManager));
        }


        private void VerifyBrokerApp(AccountManager accountManager)
        {
            if (Application.Context.PackageName.Equals(BrokerPackageName, StringComparison.OrdinalIgnoreCase))
            {
                throw new AdalException(AdalErrorEx.CannotSwitchToBrokerFromThisApp, AdalErrorMessageEx.CannotSwitchToBrokerFromThisApp);
            }

            AuthenticatorDescription authenticator = accountManager.GetAuthenticatorTypes().Where(a => a.Type == BrokerAccountType).FirstOrDefault();
            if (authenticator.Type == null)
            {
                throw new AdalException(AdalErrorEx.IncorrectBrokerAccountType, AdalErrorMessageEx.IncorrectBrokerAccountType);
            }

            VerifySignature(authenticator.PackageName);
        }

        private void VerifySignature(string brokerPackageName)
        {
            try
            {
                PackageInfo info = Application.Context.PackageManager.GetPackageInfo(brokerPackageName, PackageInfoFlags.Signatures);
                if (info == null || info.Signatures == null)
                {
                    throw new AdalException(AdalErrorEx.FailedToGetBrokerAppSignature, AdalErrorMessageEx.FailedToGetBrokerAppSignature);
                }

                // Broker App can be signed with multiple certificates. It will
                // look all of them until it finds the correct one for ADAL
                // broker.

                foreach (var signature in info.Signatures)
                {
                    MessageDigest md = MessageDigest.GetInstance("SHA");
                    md.Update(signature.ToByteArray());
                    string tag = Base64.EncodeToString(md.Digest(), Base64Flags.NoWrap);

                    // Company portal(Intune) app and Azure authenticator app
                    // have authenticator.
                    if (tag == AzureAuthenticatorAppSignature || tag == AzureAuthenticatorAppSignature2)
                    {
                        return;
                    }
                }

                throw new AdalException(AdalErrorEx.IncorrectBrokerAppSignature, AdalErrorMessageEx.IncorrectBrokerAppSignate);
            }
            catch (Android.Content.PM.PackageManager.NameNotFoundException ex)
            {
                throw new AdalException(AdalErrorEx.MissingBrokerRelatedPackage, AdalErrorMessageEx.MissingBrokerRelatedPackage, ex);
            }
            catch (NoSuchAlgorithmException ex)
            {
                throw new AdalException(AdalErrorEx.MissingDigestShaAlgorithm, AdalErrorMessageEx.MissingDigestShaAlgorithm, ex);
            }
            catch (Exception ex)
            {
                throw new AdalException(AdalErrorEx.SignatureVerificationFailed, AdalErrorMessageEx.SignatureVerificationFailed, ex);
            }
        }

        private bool VerifyAccount(AccountManager accountManager)
        {
            Account[] accountList = accountManager.GetAccountsByType(BrokerAccountType);
            return accountList == null || accountList.Length == 0;
        }

        private void AuthenticateViaBroker(AccountManager accountManager, DictionaryRequestParameters requestParameters)
        {
            var options = GetBrokerOptions(accountManager, requestParameters);
            Account account = accountManager.GetAccountsByType(BrokerAccountType).FirstOrDefault();

            if (account == null)
            {
                throw new AdalException(AdalErrorEx.NoBrokerAccountFound, AdalErrorMessageEx.NoBrokerAccountFound);
            }

            try
            {
                IAccountManagerFuture result = accountManager.GetAuthToken(account, AuthTokenType, options, false, null, new Handler(this.parameters.CallerActivity.MainLooper));

                Bundle bundleResult = (Bundle)result.GetResult(10000, TimeUnit.Milliseconds);

                authorizationResult = GetResultFromBroker(bundleResult);
                if (authorizationResult != null)
                {
                    returnedUriReady.Release();
                    return;
                }

            }
            catch (Exception ex)
            {
                throw ex;
            }

            var descriptions = accountManager.GetAuthenticatorTypes();
            var azureDescription = descriptions.Where(d => d.PackageName == "com.azure.authenticator").FirstOrDefault();
            if (azureDescription == null)
            {
                throw new AdalException("Azure authenticator not found. Either workplace join the device or set AuthorizationParameters.SkipBroker to true");
            }

            // TODO: Check authenticator's signature

            Account[] accounts = accountManager.GetAccountsByType(azureDescription.Type);
            //if (accounts.Contains(new Account()))

            Task.Factory.StartNew(() =>
            {
                Looper.Prepare();

                try
                {
                    accountManagerFeature = accountManager.AddAccount(azureDescription.Type, AuthTokenType, null, options,
                        null, null, new Handler(this.parameters.CallerActivity.MainLooper));

                    Bundle bundleResult = (Bundle)accountManagerFeature.GetResult(10000, TimeUnit.Milliseconds);

                    Intent intent = (Intent)bundleResult.GetParcelable(AccountManager.KeyIntent);
                    if (intent != null)
                    {
                        intent.PutExtra(BrokerKey.BrokerRequest, BrokerKey.BrokerRequest);
                    }

                    this.parameters.CallerActivity.StartActivityForResult(intent, 1001);
                }
                catch (Exception ex)
                {
                    if (ex != null)
                    {
                        throw ex;
                    }
                }
            });


            // Authenticator should throw OperationCanceledException if
            // token is not available
            Intent intent = bundleResult.GetParcelable(AccountManager.KeyIntent);

            // Add flag to this intent to signal that request is for broker
            // logic
            if (intent != null)
            {
                intent.PutExtra(BrokerRequest, BrokerRequest);
            }

            throw new NotFiniteNumberException(descriptions[0].CustomTokens.ToString());#1#
        }

        private void HandleMessage(Message msg)
        {
            // Making blocking request here
            var bundleResult = accountManagerFeature.GetResult(1000, TimeUnit.Milliseconds);

            returnedUriReady.Release();
        }

        private Bundle GetBrokerOptions(AccountManager accountManager, DictionaryRequestParameters requestParameters)
        {
            Bundle brokerOptions = new Bundle();
            // request needs to be parcelable to send across process
            brokerOptions.PutInt(BrokerKey.RequestId, 1); // TODO: Just a temporary number
            brokerOptions.PutString(BrokerKey.AccountAuthority, requestParameters.Authority);
            brokerOptions.PutString(BrokerKey.AdalVersionKey, GetVersionName());
            brokerOptions.PutString(BrokerKey.Resource, requestParameters[OAuthParameter.Resource]);
            brokerOptions.PutString(BrokerKey.ClientId, requestParameters[OAuthParameter.ClientId]);
            string s = GetRedirectUriForBroker();
            brokerOptions.PutString(BrokerKey.RedirectUri, s);
            // allowing single user for now
            brokerOptions.PutString(BrokerKey.LoginHint, this.GetCurrentUser(accountManager));
            brokerOptions.PutString(BrokerKey.AccountName, this.GetCurrentUser(accountManager));
            brokerOptions.PutString(BrokerKey.AccountPrompt, "Always");
            return brokerOptions;
        }

        // App needs to give permission to AccountManager to use broker.
        private void VerifyManifestPermissions()
        {
            VerifyManifestPermission("android.permission.GET_ACCOUNTS");
            VerifyManifestPermission("android.permission.MANAGE_ACCOUNTS");
            VerifyManifestPermission("android.permission.USE_CREDENTIALS");
        }

        private void VerifyManifestPermission(string permission)
        {
            if (Android.Content.PM.Permission.Granted != Application.Context.PackageManager.CheckPermission(permission, Application.Context.PackageName))
            {
                throw new AdalException(AdalErrorEx.MissingPackagePermission, string.Format(AdalErrorMessageEx.MissingPackagePermissionTemplate, permission));
            }
        }

        private string GetCurrentUser(AccountManager accountManager)
        {
            // authenticator is not used if there is not any user
            Account[] accountList = accountManager.GetAccountsByType(BrokerAccountType);
            return (accountList != null && accountList.Length > 0) ? accountList[0].Name : null;
        }

        // Version name for ADAL not for the app itself.
        private static string GetVersionName()
        {
            // Package manager does not report for ADAL
            // AndroidManifest files are not merged, so it is returning hard coded
            // value
            return "1.1.1";
        }

        private string GetRedirectUriForBroker()
        {
            string packageName = Application.Context.PackageName;

            // First available signature. Applications can be signed with multiple
            // signatures.
            string signatureDigest = this.GetCurrentSignatureForPackage(packageName);
            if (!string.IsNullOrEmpty(signatureDigest))
            {
                return string.Format("{0}://{1}/{2}", RedirectUriScheme, EncodingHelper.UrlEncode(packageName), EncodingHelper.UrlEncode(signatureDigest));
            }

            return string.Empty;
        }

        private string GetCurrentSignatureForPackage(string packageName)
        {
            try
            {
                PackageInfo info = Application.Context.PackageManager.GetPackageInfo(packageName, PackageInfoFlags.Signatures);
                if (info != null && info.Signatures != null && info.Signatures.Count > 0)
                {
                    Android.Content.PM.Signature signature = info.Signatures[0];
                    MessageDigest md = MessageDigest.GetInstance("SHA");
                    md.Update(signature.ToByteArray());
                    return Convert.ToBase64String(md.Digest(), Base64FormattingOptions.None);
                    // Server side needs to register all other tags. ADAL will
                    // send one of them.
                }
            }
            catch (Android.Content.PM.PackageManager.NameNotFoundException)
            {
                PlatformPlugin.Logger.Information(null, "Calling App's package does not exist in PackageManager");
            }
            catch (NoSuchAlgorithmException)
            {
                PlatformPlugin.Logger.Information(null, "Digest SHA algorithm does not exists");
            }

            return null;
        }

        public static AuthorizationResult GetResultFromBroker(Intent data)
        {
            string accessToken = data.GetStringExtra("account.access.token");
            DateTimeOffset expiresOn = ConvertFromTimeT(data.GetLongExtra("account.expiredate", 0));
            UserInfo userInfo = GetUserInfoFromBrokerResult(data.Extras);
            return new AuthorizationResult(accessToken, userInfo, expiresOn);
        }

        public static AuthorizationResult GetResultFromBroker(Bundle bundleResult)
        {
            if (bundleResult == null)
            {
                throw new AdalException("null_broker_result", "Broker result cannot be null");
            }

            int errorCode = bundleResult.GetInt(BrokerResultKey.ErrorCode);
            string errorMessage = bundleResult.GetString(BrokerResultKey.ErrorMessage);
            if (!string.IsNullOrEmpty(errorMessage))
            {
                /*ADALError adalErrorCode = ADALError.BROKER_AUTHENTICATOR_ERROR_GETAUTHTOKEN;
                switch (errCode)
                {
                    case BrokerResultKey.ERROR_CODE_BAD_ARGUMENTS:
                        adalErrorCode = ADALError.BROKER_AUTHENTICATOR_BAD_ARGUMENTS;
                        break;
                    case ACCOUNT_MANAGER_ERROR_CODE_BAD_AUTHENTICATION:
                        adalErrorCode = ADALError.BROKER_AUTHENTICATOR_BAD_AUTHENTICATION;
                        break;
                    case AccountManager.ERROR_CODE_UNSUPPORTED_OPERATION:
                        adalErrorCode = ADALError.BROKER_AUTHENTICATOR_UNSUPPORTED_OPERATION;
                        break;
                }*/

                throw new AdalException(AdalError.Unknown, errorMessage);
            }
            else
            {
                /*bool initialRequest = bundleResult.GetBoolean(BrokerResultKey.AccountInitialRequest);
                if (initialRequest)
                {
                    // Initial request from app to Authenticator needs to launch
                    // prompt
                    return AuthenticationResult.createResultForInitialRequest();
                }*/

                string accessToken = bundleResult.GetString("authtoken");
                return accessToken != null ? new AuthorizationResult(accessToken, GetUserInfoFromBrokerResult(bundleResult), DateTimeOffset.Now) : null;
            }
        }

        private static UserInfo GetUserInfoFromBrokerResult(Bundle bundle)
        {
            // Broker has one user and related to ADFS WPJ user. It does not return idtoken
            return new UserInfo
            {
                UniqueId = bundle.GetString("account.userinfo.userid"),
                GivenName = bundle.GetString("account.userinfo.given.name"),
                FamilyName = bundle.GetString("account.userinfo.family.name"),
                DisplayableId = bundle.GetString("account.userinfo.userid.displayable"),
                IdentityProvider = bundle.GetString("account.userinfo.identity.provider"),
            };
        }

        private static DateTimeOffset ConvertFromTimeT(long seconds)
        {
            var startTime = new DateTimeOffset(1970, 1, 1, 0, 0, 0, 0, TimeSpan.Zero);
            return startTime.AddMilliseconds(seconds);
        }

        public Task<AuthorizationResult> AcquireAuthorizationAsync(Uri authorizationUri, Uri redirectUri, CallState callState)
        {
            throw new NotImplementedException();
        }
    }

    class CallBackHandler : Java.Lang.Object, IAccountManagerCallback
    {
        public void Run(IAccountManagerFuture future)
        {
        }
    }
}