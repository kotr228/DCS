namespace GrayCat.Core.Models.BlockLibrary;

public class NavbarBlock : BaseBlock
{
    public override string BlockType => "Navbar";
    public override string DisplayName => "Navigation Bar";
    public override string Description => "Site navigation with logo and menu items";

    public override Dictionary<string, object> DefaultProperties => new()
    {
        { "logo", "" },
        { "brandName", "Brand" },
        { "position", "static" }, // static, fixed, sticky
        { "transparent", false },
        { "menuItems", "[{\"text\":\"Home\",\"url\":\"#\"},{\"text\":\"About\",\"url\":\"#about\"},{\"text\":\"Contact\",\"url\":\"#contact\"}]" },
        { "showSearch", false },
        { "ctaText", "" },
        { "ctaUrl", "#" }
    };

    public override string GenerateReactCode()
    {
        return @"
import React, { useState } from 'react';
import './NavbarBlock.css';

const NavbarBlock = ({
  logo,
  brandName,
  position,
  transparent,
  menuItems,
  showSearch,
  ctaText,
  ctaUrl
}) => {
  const [isOpen, setIsOpen] = useState(false);
  const items = typeof menuItems === 'string' ? JSON.parse(menuItems) : menuItems;
  
  return (
    <nav className={`navbar navbar-${position} ${transparent ? 'navbar-transparent' : ''}`}>
      <div className='navbar-container'>
        <div className='navbar-brand'>
          {logo ? <img src={logo} alt={brandName} /> : <span>{brandName}</span>}
        </div>
        
        <button 
          className='navbar-toggle' 
          onClick={() => setIsOpen(!isOpen)}
          aria-label='Toggle menu'
        >
          ☰
        </button>
        
        <div className={`navbar-menu ${isOpen ? 'navbar-menu-open' : ''}`}>
          {items.map((item, i) => (
            <a key={i} href={item.url} className='navbar-item'>
              {item.text}
            </a>
          ))}
          
          {showSearch && (
            <input type='search' placeholder='Search...' className='navbar-search' />
          )}
          
          {ctaText && (
            <a href={ctaUrl} className='navbar-cta'>
              {ctaText}
            </a>
          )}
        </div>
      </div>
    </nav>
  );
};

export default NavbarBlock;";
    }
}