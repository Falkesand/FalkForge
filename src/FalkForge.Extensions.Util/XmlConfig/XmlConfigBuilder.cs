namespace FalkForge.Extensions.Util.XmlConfig;

public sealed class XmlConfigBuilder
{
    private string _id = string.Empty;
    private string _filePath = string.Empty;
    private string _xPath = string.Empty;
    private XmlConfigAction _action;
    private string? _elementName;
    private string? _attributeName;
    private string? _value;
    private int _sequence;
    private string? _componentRef;

    public XmlConfigBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public XmlConfigBuilder File(string filePath)
    {
        _filePath = filePath;
        return this;
    }

    public XmlConfigBuilder XPath(string xPath)
    {
        _xPath = xPath;
        return this;
    }

    public XmlConfigBuilder CreateElement(string elementName)
    {
        _action = XmlConfigAction.CreateElement;
        _elementName = elementName;
        return this;
    }

    public XmlConfigBuilder DeleteElement()
    {
        _action = XmlConfigAction.DeleteElement;
        return this;
    }

    public XmlConfigBuilder SetAttribute(string attributeName, string value)
    {
        _action = XmlConfigAction.SetAttribute;
        _attributeName = attributeName;
        _value = value;
        return this;
    }

    public XmlConfigBuilder DeleteAttribute(string attributeName)
    {
        _action = XmlConfigAction.DeleteAttribute;
        _attributeName = attributeName;
        return this;
    }

    public XmlConfigBuilder SetValue(string value)
    {
        _action = XmlConfigAction.SetValue;
        _value = value;
        return this;
    }

    public XmlConfigBuilder BulkSetValue(string value)
    {
        _action = XmlConfigAction.BulkSetValue;
        _value = value;
        return this;
    }

    public XmlConfigBuilder Sequence(int sequence)
    {
        _sequence = sequence;
        return this;
    }

    public XmlConfigBuilder ComponentRef(string componentRef)
    {
        _componentRef = componentRef;
        return this;
    }

    public Result<XmlConfigModel> Build()
    {
        var model = new XmlConfigModel
        {
            Id = _id,
            FilePath = _filePath,
            XPath = _xPath,
            Action = _action,
            ElementName = _elementName,
            AttributeName = _attributeName,
            Value = _value,
            Sequence = _sequence,
            ComponentRef = _componentRef
        };

        var validationResult = XmlConfigValidator.Validate(model);
        if (validationResult.IsFailure)
            return Result<XmlConfigModel>.Failure(validationResult.Error);

        return model;
    }
}
