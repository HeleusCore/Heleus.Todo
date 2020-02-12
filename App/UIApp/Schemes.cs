using System;
using System.Threading.Tasks;
using Heleus.Apps.Shared;
using Heleus.Base;
using Heleus.Chain;
using Heleus.Cryptography;

namespace Heleus.Apps.Todo
{
    public class RequestInvitationSchemeAction : ServiceNodeSchemeAction
    {
        public const string ActionName = "requestinvitation";

        public readonly long AccountId;

        public override bool IsValid => AccountId > 0;


        public RequestInvitationSchemeAction(SchemeData schemeData) : base(schemeData)
        {
            GetLong(StartIndex, out AccountId);
        }

        public override async Task Run()
        {
            if (!IsValid)
                return;

            var serviceNode = await GetServiceNode();
            if (serviceNode == null)
                return;

            var app = UIApp.Current;
            if (app?.CurrentPage != null)
            {
                await app.CurrentPage.Navigation.PushAsync(new InvitationPage(serviceNode, 0, AccountId));
            }
        }
    }

    public class InvitationSchemeAction : ServiceNodeSchemeAction
    {
        public const string ActionName = "invitation";

        public readonly long ListId;
        public readonly long AccountId;
        public readonly long SenderAccountId;

        readonly string _exportedSecretKeyHex;

        public SecretKey SecretKey { get; private set; }

        public override bool IsValid => ListId > 0 && AccountId > 0 && !string.IsNullOrEmpty(_exportedSecretKeyHex);

        public InvitationSchemeAction(SchemeData schemeData) : base(schemeData)
        {
            GetLong(StartIndex, out ListId);
            GetLong(StartIndex + 1, out AccountId);
            GetLong(StartIndex + 2, out SenderAccountId);
            GetInt(StartIndex + 3, out var encrypted);

            RequiresPassword = encrypted != 0;

            try
            {
                _exportedSecretKeyHex = GetString(StartIndex + 4);
                if(!RequiresPassword)
                {
                    SecretKey = new ExportedSecretKey(_exportedSecretKeyHex).Decrypt(ListId.ToString() + AccountId + SenderAccountId);
                }
            }
            catch { }
        }

        public override Task<bool> Decrypt(string password)
        {
            try
            {
                SecretKey = new ExportedSecretKey(_exportedSecretKeyHex).Decrypt(password + ListId + AccountId + SenderAccountId);
                return Task.FromResult(SecretKey != null);
            }
            catch (Exception ex)
            {
                Log.IgnoreException(ex);
            }
            return Task.FromResult(false);
        }

        public override async Task Run()
        {
            if (!IsValid)
                return;

            var serviceNode = await GetServiceNode(AccountId);
            if (serviceNode == null)
                return;

            var app = UIApp.Current;
            if (app?.CurrentPage != null)
            {
                if(SecretKey != null)
                    await app.CurrentPage.Navigation.PushAsync(new HandleInvitationPage(serviceNode, this));
                else
                    await app.CurrentPage.Navigation.PushAsync(new HandleRequestPage(RequestUri));
            }
        }
    }
}
