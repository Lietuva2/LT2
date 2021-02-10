(function($){
function log(){
  if ($.preview !== undefined && $.preview.debug && window.console){
    console.log(Array.prototype.slice.call(arguments));
  }
}
/*!
 * linkify - v0.3 - 6/27/2009
 * http://benalman.com/code/test/js-linkify/
 * 
 * Copyright (c) 2009 "Cowboy" Ben Alman
 * Licensed under the MIT license
 * http://benalman.com/about/license/
 * 
 * Some regexps adapted from http://userscripts.org/scripts/review/7122
 */

// Turn text into linkified html.
// 
// var html = linkify( text, options );
// 
// options:
// 
//  callback (Function) - default: undefined - if defined, this will be called
//    for each link- or non-link-chunk with two arguments, text and href. If the
//    chunk is non-link, href will be omitted.
// 
//  punct_regexp (RegExp | Boolean) - a RegExp that can be used to trim trailing
//    punctuation from links, instead of the default.
// 
// This is a work in progress, please let me know if (and how) it fails!
function Selector(form, selector) {

  //Base Selector
  var Selector = {
    selector : '.selector',
    type : 'small',
    template : null,
    elem : null,
    templates : {
      'small': [
        '<div class="clear"></div>',
        '<div class="selector small">',
          '<div class="thumbnail">',
          '<div class="controls">',
            '<a class="nothumb" href="#">&#10005;</a>',
          '</div>',
          '<div class="items">',
            '<div class="images">',
              '<a class="thumbnail_image" href="#">',
                '<img src="{0}"/>',
               '</a>',
            '</div>',
          '</div>',
        '</div>',
          '<div class="attributes">',
            '<a class="title" href="#"><a class="title" href="#">{1}</a></a>',
            '<p><a class="description" href="#"><p><a class="description" href="#">{2}</a></p></a></p>',
            '<input type="text" id="id_thumbnail_url" name="thumbnail_url" class="thumbnail_url hide" placeholder="Įveskite nuorodą į paveiksliuką" />',
          '</div>',
          '<div class="action"><a href="#" class="close">&#10005;</a></div>',
        '</div>'].join(''),
      'photo': [
        '<div class="item selector photo">',
        '<a class="title" href="{0}" target="_blank">{1}</a>',
        '<div class="media image">',
            '<img title="{1}" src="{0}"/>',
        '</div>',
        '<div class="">',
            '<p class="description">{2}</p>',
        '</div>',
        '<div class="clearfix"></div>',
    '</div>'].join('')
    },

    // If a developer wants complete control of the selector, they can
    // override the render function.
    render : function (obj) {
      // If the #selector ID is there then replace it with the template. Just
      // tells us where it should be on the page.
      var template = null;

      if (this.template !== null) {
        template = this.template;
      } else if (this.templates[obj.type]) {
          template = this.templates[obj.type];
      } else {
          template = this.templates[this.type];
      }

      var view = this.toView(obj);

      var html = $.helpers.format(template, typeof view.thumbnail_url != "undefined" ? view.thumbnail_url : view.url,
          typeof view.title != "undefined" ? view.title : "",
          typeof view.description != "undefined" ? view.description : "");

      // If the developer told us where to put the selector, put it there.
      if (form.find(this.selector).length) {
        this.elem = form.find(this.selector).replaceWith(html);
      } else {
        this.elem = form.append(html);
      }

      // We need to keep track of the selector elem so we don't have to do
      // form.find(this.selector) all the time.
      this.elem = form.find(this.selector);

      // Selector may be hidden. Let's show it.
      this.elem.show();

      this.bind();
    },
    // To View. Only exists to be overwritten basiclly.
    toView : function (obj) {
      if(obj.type == 'photo') {
          obj.thumbnail_url = obj.url;
      }
      if(!obj.thumbnail_url) {
          obj.thumbnail_url = $.helpers.resolveUrl("~/Content/Images/noimage.png");
      }
      return obj;
    },
    toPartials : function (obj) {
      // Clone partials for later.
      var p = $.extend(true, {}, this.partials);

      return p;
    },
    //Clears the selector post submit.
    clear : function (e) {
      if (e !== undefined) {
        e.preventDefault();
      }
        if(this.elem) {
            this.elem.html('');
            this.elem.hide();
        }
        if(form) {
            form.find('input[type="hidden"].preview_input').remove();
            form.find('#id_original_url').remove();
        }
    },
    nothumb : function (e) {
      e.preventDefault();
      this.elem.find('.thumbnail').hide();
      form.find('#id_thumbnail_url').val('');
    },
    // When a user wants to Edit a title or description we need to switch out
    // an input or text area
    title : function (e) {
      e.preventDefault();
      //overwrite the a tag with the value of the tag.
      var elem = $('<input/>').attr({
        'value' : $(e.target).text(),
        'class' : 'title',
        'type' : 'text'
      });

      $(e.target).replaceWith(elem);

      //Set the focus on this element
      elem.focus();

      // Sets up for another bind.
      var t = this.title;

      // puts the a tag back on blur. It's a single bind so it will be
      // trashed on blur.
      elem.one('blur', function (e) {
        var elem = $(e.target);
        // Sets the New Title in the hidden inputs
        form.find('#id_title').val(elem.val());

        var a = $('<a/>').attr({
            'class': 'title',
            'href' : '#'
          }).text(elem.val());

        $(e.target).replaceWith(a);

        // Bind it back again.
        a.bind('click', t);
      });
    },
    //Same as before, but for description
    description : function (e) {
      e.preventDefault();
      //overwrite the a tag with the value of the tag.
      var elem = $('<textarea/>').attr({
        'class' : 'description'
      }).text($(e.target).text());

      $(e.target).replaceWith(elem);

      //Set the focus on this element
      elem.focus();

      // Sets up for another bind.
      var d = this.description;

      // puts the a tag back on blur. It's a single bind so it will be
      // trashed on blur.
      elem.one('blur', function (e) {
        var elem = $(e.target);
        // Sets the New Title in the hidden inputs
        form.find('#id_description').val(elem.val());

        var a = $('<a/>').attr({
            'class': 'description',
            'href': '#'
          }).text(elem.val());

        $(e.target).replaceWith(a);

        // Bind it back again.
        a.bind('click', d);

      });
    },
    //Same as before, but for thumbnail_url
    thumbnail : function (e) {
        e.preventDefault();
        var elem = form.find('.thumbnail_url');
        
        elem.show();
        elem.focus();
    },
    thumbnail_url_blur : function(e) {
        var elem = form.find('.thumbnail_url');
        form.find('#id_thumbnail_url').val(elem.val());
        form.find('.thumbnail_url').hide();
        var img = form.find('.thumbnail img');
        img.attr('src', elem.val());
        img.show();
    },
    update : function (e) {
      //this.elem.find('.' + $(e.target).attr('name')).text($(e.target).val());
    },
    // Binds the correct events for the controls.
    bind : function () {
      // Thumbnail Controls
      this.elem.find('.nothumb').bind('click', this.nothumb);

      // Binds the close button.
      this.elem.find('.action .close').bind('click', this.clear);
      this.elem.bind('mouseenter mouseleave', function () {
        $(this).find('.action').toggle();
      });

      //Show hide the controls.
        this.elem.on('mouseenter', '.thumbnail', function () {
            $(this).find('.controls').show();
        });
        
        this.elem.on('mouseleave', '.thumbnail', function () {
            $(this).find('.controls').hide();
        });

      //Edit Title and Description handlers.
      this.elem.find('.title').bind('click', this.title);
      this.elem.find('.description').bind('click', this.description);
      this.elem.find('.thumbnail a.thumbnail_image').bind('click', this.thumbnail);
      this.elem.find('.thumbnail_url').bind('blur', this.thumbnail_url_blur);
    }
  };

  // Overwrites any funtions
  _.extend(Selector, selector);
  _.bindAll(Selector);

  return Selector;
}

/* jQuery Preview - v0.2
 *
 * jQuery Preview is a plugin by Embedly that allows developers to create tools
 * that enable users to share links with rich previews attached.
 *
 */

// Base Preview Object. Holds a common set of functions to interact with
// Embedly's Preview endpoint.
function Preview(elem, options) {

  //Preview Object that We build from the options passed into the function
  var Preview = {

    // List of all the attrs that can be sent to Embedly
    api_args : ['key', 'maxwidth', 'maxheight', 'width', 'wmode', 'autoplay',
      'videosrc', 'allowscripts', 'words', 'chars', 'secure', 'frame'],

    // What attrs we are going to use.
    display_attrs : ['type', 'url', 'title', 'description',
       'provider_url', 'provider_name', 'html',
      'thumbnail_url', 'version', 'author_name', 'author_url', 'cache_age', 'width', 'height'],
      //'favicon_url','provider_display', 'safe', 'object_type', 'image_url'
    default_data : {},
    debug : false,
    form : null,
    type : 'link',
    loading_selector : '.loader',
    options : {
      debug : false,
      selector : {},
      field : null,
      preview : {},
      wmode : 'opaque',
      words : 30
      //maxwidth : 478
    },

    init : function (elem, settings) {

      // Sets up options
      this.options = _.extend(this.options, typeof settings !== "undefined" ? settings : {});
        var opts = this.options;
      // Sets up the data args we are going to send to the API
      var data = {};
      _.each(_.intersection(_.keys(this.options), this.api_args), function (n) {
        var v = opts[n];
        // 0 or False is ok, but not null or undefined
        if (!(_.isNull(v) || _.isUndefined(v))) {
          data[n] = v;
        }
      });
      this.default_data = data;

      // Just reminds us which form we should be working on.
      this.form = null;
      if (elem){
        this.form = options.form ? options.form : elem.parents('form');
      }

      //Debug used for logging
      this.debug = this.options.debug;

      //Sets up Selector
      this.selector = Selector(this.form, this.options.selector);

      // Overwrites any funtions
      _.extend(this, this.options.preview);

      // Binds all the functions that you want.
      if (elem){
        this.bind();
      }
    },
    /*
     * Utils for handling the status.
     */
    getStatusUrl : function (obj) {
      // Grabs the status out of the Form.
      var status = elem.val();

      //ignore the status it's blank.
      if (status === '') {
        return null;
      }

      // Simple regex to make sure the url with a scheme is valid.
      var urlexp = /^http(s?):\/\/(\w+:{0,1}\w*)?(\S+)(:[0-9]+)?(\/|\/([\w#!:.?+=&%@!\-\/]))?/i;
      var matches = status.match(urlexp);

      var url = matches? matches[0] : null;

      //No urls is the status. Try for urls without scheme i.e. example.com
      if (url === null) {
        urlexp = /[-\w]+(\.[a-z]{2,})+(\S+)?(\/|\/[\w#!:.?+=&%@!\-\/])?/gi;
        matches = status.match(urlexp);
        url = matches? 'http://'+matches[0] : null;
      }

      //Note that in both cases we only grab the first URL.
      return url;
    },
    toggleLoading : function (show) {
        var btn = this.form.find('input[type="submit"], button[type="submit"]');
        if(show) {
            btn.attr('disabled', 'disabled');
            $.helpers.showLoader(this.form);
        }
        else {
           btn.removeAttr('disabled');
            $.helpers.hideLoader();
        }
    },
    callback : function(obj) {
      // empty. can be overridden by user
    },
    clearSelector: function () {
        this.selector.clear();  
    },
    //Metadata Callback
    _callback : function (obj) {
      //tells the loader to stop
      this.toggleLoading(false);

      // Here is where you actually care about the obj
      log(obj);

      // Every obj should have a 'type'. This is a clear sign that
      // something is off. This is a basic check to make sure we should
      // proceed. Generally will never happen.
      if (!obj.hasOwnProperty('type')) {
        log('Embedly returned an invalid response');
        this.error(obj);
        return false;
      }

      // Error. If the url was invalid, or 404'ed or something else. The
      // endpoint will pass back an obj  of type 'error'. Generally this is
      // were the default workflow should happen.
      if (obj.type === 'error') {
        log('URL ('+obj.url+') returned an error: '+ obj.error_message);
        this.error(obj);
        return false;
      }

        var _this = this;

      // If this is a change in the URL we need to delete all the old
      // information first.
      this.form.find('input[type="hidden"].preview_input').remove();

      // Now use the selector obj to render the selector.
      this.selector.render(obj);
        
      //Sets all the data to a hidden inputs for the post.
      var form = this.form;
      _.each(this.display_attrs, function (n) {
          var v = obj[n];
          if(n == 'html') {
              v = $.helpers.htmlEncode(obj[n]);
          }

          var d = {
          name : n,
          type : 'hidden',
          id : 'id_'+n,
          value : v
        };

        // It's possible that the title or description or something else is
        // already in the form. If it is then we need to Love them for who they
        // are and fill in values.
        var e = form.find('#id_'+n);

        if(e.length) {
          // It's hidden, use it
          if (e.attr('type') === 'hidden') {
            // jQuery doesn't allow changing the 'type' attribute
            delete d.type;
            
            e.attr(d);
          } else{
            // Be careful here.
            if (!e.val()) {
              e.val(obj[n]);
            } else {
              // Use the value in the obj
              obj[n] = e.val();
            }
            // Bind updates to the select.
            e.bind('keyup', function (e) {
              _this.selector.update(e);
            });
          }
          e.addClass('preview_input');
        } else{
          d['class'] ='preview_input';
          form.append($('<input />').attr(d));
        }
      });

      this.callback(obj);
    },
    // Used as a generic error callback if something fails.
    error : function () {
      log('error');
      log(arguments);
    },
    // Actually makes the ajax call to Embedly. We make this a seperate
    // function because implementations like Chrome Plugins need to overwrite
    // how the call is made.
    ajax : function(data){
      // Make the request to Embedly. Note we are using the
      // preview endpoint: http://embed.ly/docs/endpoints/1/preview
      $.ajax({
        url: 'https://api.embed.ly/1/oembed',
        dataType: 'jsonp',
        data: data,
        success: this._callback,
        error: this.error
      });
    },
    // Fetches the Metadata from the Embedly API
    fetch: function (url) {
        // Get a url out of the status box unless it was passed in.
        if (typeof url === 'undefined' || typeof url !== 'string') {
            url = this.getStatusUrl();
        }

        // If there is no url return false.
        if (url === null) return true;

        //We need to trim the URL.
        url = $.trim(url);

        // If we already looked for a url, there will be an original_url hidden
        // input that we should look for and compare values. If they are the
        // same we will ignore.
        var originalEl = this.form.find('#id_original_url');
        if (originalEl.length == 0) {
            originalEl = $('<input name="original_url" type="hidden" id="id_original_url">').appendTo(this.form);
        }

        if (originalEl.val() === url) {
            return true;
        } else {
            originalEl.val(url);
        }

        //Tells the loaded to start
        this.toggleLoading(true);

        //sets up the data we are going to use in the request.
        var data = _.clone(this.default_data);
        data.url = url;

        // make the ajax call
        this.ajax(data);

        return true;
    },
    keyUp : function (e) {
      // Only respond to keys that insert whitespace (spacebar, enter)
      if (e.which !== 32 && e.which !== 13) {
        return null;
      }

      //See if there is a url in the status textarea
      var url = this.getStatusUrl();
      if (url === null) {
        return null;
      }
      log('onKeyUp url:'+url);

      // If there is a url, then we need to unbind the event so it doesn't fire
      // again. This is very common for all status updaters as otherwise it
      // would create a ton of unwanted requests.
      $(this.status_selector).unbind('keyup');

      //Fire the fetch metadata function
      this.fetch(url);
    },
    paste : function (e) {
      //We delay the fire on paste.
      _.delay(this.fetch, 200);
    },
    //The submit post back that readys the data for the actual submit.
    //Bind a bunch of functions.
    bind : function () {
      log('Starting Bind');

      // Bind Keyup, Blur and Paste
      //elem.bind('blur', this.fetch);
      elem.bind('keyup', this.keyUp);
      elem.bind('paste', this.paste);
      // the event `attach` tells fetch to run on the input.
      elem.bind('attach', this.fetch);

    }
  };

  //Use Underscore to make ``this`` not suck.
  _.bindAll(Preview);

  //Bind Preview Function
  Preview.init(elem, options);

  //Return the Preview Function that will eventually be namespaced to $.preview.
  return Preview;
}

  // Sets up an initial $.preview for use before the form is initialized.
  // Useful for early access to display if needed.
  $.preview = new Preview(null, {});
    $.preview.instances = new Array();

  //Set up the Preview Functions for jQuery
    $.fn.preview = function(options, callback) {
        $(this).each(function (i, e) {
            if (!$(this).data('preview')) {
                var inst = new Preview($(this), options);
                $.preview.instances[$.preview.instances.length] = inst;
                $(this).data('preview', inst);
            }
        });

        return this;
    };
})(jQuery);
