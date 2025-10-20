namespace GrayCat.Core.Models.BlockLibrary;

public class ButtonBlock : BaseBlock
{
    public override string BlockType => "Button";
    public override string DisplayName => "Button Block";
    public override string Description => "Interactive button with customizable text and action";

    public override Dictionary<string, object> DefaultProperties => new()
    {
        { "text", "Click Me" },
        { "url", "#" },
        { "variant", "primary" }, // primary, secondary, success, danger
        { "size", "medium" }, // small, medium, large
        { "fullWidth", false },
        { "disabled", false },
        { "icon", "" }, // optional icon name
        { "target", "_self" } // _self, _blank
    };

    public override string GenerateReactCode()
    {
        return @"
import React from 'react';
import './ButtonBlock.css';

const ButtonBlock = ({ 
  text = 'Click Me', 
  url = '#', 
  variant = 'primary',
  size = 'medium',
  fullWidth = false,
  disabled = false,
  icon = '',
  target = '_self'
}) => {
  const className = `btn btn-${variant} btn-${size} ${fullWidth ? 'btn-full' : ''}`;
  
  return (
    <a 
      href={url} 
      className={className}
      target={target}
      rel={target === '_blank' ? 'noopener noreferrer' : undefined}
      aria-disabled={disabled}
    >
      {icon && <span className={`icon icon-${icon}`}></span>}
      <span>{text}</span>
    </a>
  );
};

export default ButtonBlock;";
    }
}