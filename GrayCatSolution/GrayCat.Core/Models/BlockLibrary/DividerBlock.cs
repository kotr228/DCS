namespace GrayCat.Core.Models.BlockLibrary;
public class DividerBlock : BaseBlock
{
    public override string BlockType => "Divider";
    public override string DisplayName => "Divider/Separator";
    public override string Description => "Visual separator between sections";

    public override Dictionary<string, object> DefaultProperties => new()
    {
        { "style", "solid" }, // solid, dashed, dotted, double
        { "thickness", 1 },
        { "color", "#DDDDDD" },
        { "spacing", "medium" }, // small, medium, large
        { "withIcon", false },
        { "icon", "★" }
    };

    public override string GenerateReactCode()
    {
        return @"
import React from 'react';
import './DividerBlock.css';

const DividerBlock = ({ style, thickness, color, spacing, withIcon, icon }) => {
  return (
    <div className={`divider divider-${spacing}`}>
      {withIcon ? (
        <div className='divider-with-icon'>
          <span className='divider-line' style={{ borderColor: color, borderWidth: thickness, borderStyle: style }}></span>
          <span className='divider-icon'>{icon}</span>
          <span className='divider-line' style={{ borderColor: color, borderWidth: thickness, borderStyle: style }}></span>
        </div>
      ) : (
        <hr style={{ borderColor: color, borderWidth: thickness, borderStyle: style }} />
      )}
    </div>
  );
};

export default DividerBlock;";
    }
}