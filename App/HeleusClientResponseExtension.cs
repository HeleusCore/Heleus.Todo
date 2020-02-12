using Heleus.Apps.Shared;
using Heleus.Network.Client;
using Heleus.TodoService;
using Heleus.Transactions;
using Heleus.Transactions.Features;

namespace Heleus.Apps.Todo
{
    public static class HeleusClientResponseExtension
    {
        public static string GetErrorMessage(this HeleusClientResponse response)
        {
            if (response.ResultType != HeleusClientResultTypes.Ok)
            {
                return Tr.Get("HeleusClientResultTypes." + response.ResultType);
            }

            if(response.TransactionResult == TransactionResultTypes.FeatureCustomError)
            {
                return Tr.Get(Feature.GetFeatureErrorString(response.UserCode));
            }

            if (response.TransactionResult == TransactionResultTypes.ChainServiceErrorResponse)
            {
                return Tr.Get("ServiceUserCodes", Tr.Get("ServiceUserCodes." + (ServiceUserCodes)response.UserCode), response.UserCode);
            }

            return Tr.Get("TransactionResult", Tr.Get("TransactionResultTypes." + response.TransactionResult));
        }
    }
}
