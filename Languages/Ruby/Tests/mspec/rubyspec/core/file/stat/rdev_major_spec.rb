require File.dirname(__FILE__) + '/../../../spec_helper'
require File.dirname(__FILE__) + '/fixtures/classes'

describe "File::Stat#rdev_major" do
  platform_is_not :windows do
    it "returns the major part of File::Stat#rdev" do
      File.stat(FileStatSpecs.null_device).rdev_major.should be_kind_of(Integer)
    end
  end

  platform_is :windows do
    it "returns nil" do
      File.stat(FileStatSpecs.null_device).rdev_major.should be_nil
    end
  end
end
