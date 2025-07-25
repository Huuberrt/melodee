using Melodee.Common.Data.Models;

namespace Melodee.Common.Models;

public record ServiceResult<T>(Setting ExistingSetting) : OperationResult<Setting?>;
