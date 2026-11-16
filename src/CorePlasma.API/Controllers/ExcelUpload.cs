using CorePlasma.Common.Exceptions;

namespace CorePlasma.API.Controllers;

internal static class ExcelUpload
{
    private static readonly string[] AllowedExtensions = [".xlsx", ".xls"];

    /// <summary>Validates the upload and buffers it (the Excel reader needs a seekable stream).</summary>
    public static async Task<MemoryStream> ToMemoryStreamAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            throw new BusinessException("Please upload a non-empty Excel file in the 'file' field.");
        }

        if (!AllowedExtensions.Contains(Path.GetExtension(file.FileName), StringComparer.OrdinalIgnoreCase))
        {
            throw new BusinessException("Only .xlsx or .xls files are supported.");
        }

        var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return buffer;
    }
}
