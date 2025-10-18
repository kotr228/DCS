namespace GrayCat.Core.Models.BlockLibrary;

public class HeaderBlock : BaseBlock
{
    public override string BlockType => "Header";
    public override string DisplayName => "Header Block";
    public override string Description => "Page header with title and navigation";

    public override Dictionary<string, object> DefaultProperties => new()
    {
        { "title", "Welcome" },
        { "subtitle", "" },
        { "showNavigation", true },
        { "backgroundColor", "#ffffff" },
        { "textColor", "#000000" }
    };

    public override string GenerateReactCode()
    {
        return @"
import React from 'react';

const Header = ({ title, subtitle, showNavigation, backgroundColor, textColor }) => {
  return (
    <header style={{ backgroundColor, color: textColor }}>
      <h1>{title}</h1>
      {subtitle && <p>{subtitle}</p>}
      {showNavigation && (
        <nav>
          <a href=""#home"">Home</a>
          <a href=""#about"">About</a>
          <a href=""#contact"">Contact</a>
        </nav>
      )}
    </header>
  );
};

export default Header;";
    }
}