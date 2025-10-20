namespace GrayCat.Core.Models.BlockLibrary;

public class ContactFormBlock : BaseBlock
{
    public override string BlockType => "ContactForm";
    public override string DisplayName => "Contact Form";
    public override string Description => "Form for collecting user messages with validation";

    public override Dictionary<string, object> DefaultProperties => new()
    {
        { "title", "Get in Touch" },
        { "subtitle", "We'd love to hear from you" },
        { "fields", "[{\"name\":\"name\",\"type\":\"text\",\"label\":\"Name\",\"required\":true},{\"name\":\"email\",\"type\":\"email\",\"label\":\"Email\",\"required\":true},{\"name\":\"message\",\"type\":\"textarea\",\"label\":\"Message\",\"required\":true}]" },
        { "submitText", "Send Message" },
        { "submitUrl", "/api/contact" },
        { "successMessage", "Thank you! We'll get back to you soon." },
        { "errorMessage", "Oops! Something went wrong. Please try again." },
        { "showPhoneField", true },
        { "showSubjectField", false },
        { "layout", "vertical" } // vertical, horizontal
    };

    public override string GenerateReactCode()
    {
        return @"
import React, { useState } from 'react';
import './ContactFormBlock.css';

const ContactFormBlock = ({
  title,
  subtitle,
  fields,
  submitText,
  submitUrl,
  successMessage,
  errorMessage,
  showPhoneField,
  showSubjectField,
  layout
}) => {
  const [formData, setFormData] = useState({});
  const [status, setStatus] = useState('idle'); // idle, loading, success, error
  const [errors, setErrors] = useState({});
  
  const formFields = typeof fields === 'string' ? JSON.parse(fields) : fields;
  
  const validateEmail = (email) => {
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
  };
  
  const validate = () => {
    const newErrors = {};
    formFields.forEach(field => {
      if (field.required && !formData[field.name]) {
        newErrors[field.name] = `${field.label} is required`;
      }
      if (field.type === 'email' && formData[field.name] && !validateEmail(formData[field.name])) {
        newErrors[field.name] = 'Invalid email address';
      }
    });
    return newErrors;
  };
  
  const handleSubmit = async (e) => {
    e.preventDefault();
    const validationErrors = validate();
    
    if (Object.keys(validationErrors).length > 0) {
      setErrors(validationErrors);
      return;
    }
    
    setStatus('loading');
    setErrors({});
    
    try {
      const response = await fetch(submitUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(formData)
      });
      
      if (response.ok) {
        setStatus('success');
        setFormData({});
      } else {
        setStatus('error');
      }
    } catch (err) {
      setStatus('error');
    }
  };
  
  const handleChange = (name, value) => {
    setFormData(prev => ({ ...prev, [name]: value }));
    if (errors[name]) {
      setErrors(prev => ({ ...prev, [name]: '' }));
    }
  };
  
  return (
    <div className={`contact-form contact-form-${layout}`}>
      <div className='contact-form-header'>
        <h2>{title}</h2>
        {subtitle && <p>{subtitle}</p>}
      </div>
      
      {status === 'success' ? (
        <div className='contact-form-success'>{successMessage}</div>
      ) : (
        <form onSubmit={handleSubmit}>
          {formFields.map(field => (
            <div key={field.name} className='form-group'>
              <label htmlFor={field.name}>
                {field.label}
                {field.required && <span className='required'>*</span>}
              </label>
              
              {field.type === 'textarea' ? (
                <textarea
                  id={field.name}
                  name={field.name}
                  value={formData[field.name] || ''}
                  onChange={(e) => handleChange(field.name, e.target.value)}
                  rows={5}
                  className={errors[field.name] ? 'error' : ''}
                />
              ) : (
                <input
                  type={field.type}
                  id={field.name}
                  name={field.name}
                  value={formData[field.name] || ''}
                  onChange={(e) => handleChange(field.name, e.target.value)}
                  className={errors[field.name] ? 'error' : ''}
                />
              )}
              
              {errors[field.name] && (
                <span className='error-message'>{errors[field.name]}</span>
              )}
            </div>
          ))}
          
          {status === 'error' && (
            <div className='contact-form-error'>{errorMessage}</div>
          )}
          
          <button 
            type='submit' 
            disabled={status === 'loading'}
            className='contact-form-submit'
          >
            {status === 'loading' ? 'Sending...' : submitText}
          </button>
        </form>
      )}
    </div>
  );
};

export default ContactFormBlock;";
    }
}